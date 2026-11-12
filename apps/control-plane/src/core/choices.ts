import { createHash } from 'node:crypto';
import type {
  ChoiceList,
  ChoiceRow,
  ChoiceRowKind,
  CommandType,
  HereScope,
  RuntimeDescriptor,
} from '@halcyonic/contracts';
import type { Projection } from '@halcyonic/domain';

/** At most this many rows are shown of a list; `total` says how many there are. */
export const CHOICE_ROWS_SHOWN = 32;

/** A row as the host holds it: what the person or the assistant sees, and the value its index stands for. */
export interface HeldRow {
  readonly row: ChoiceRow;
  /** The id or the word the payload takes for this row; never shown, never the model's. */
  readonly value: string;
}

export interface ChoiceSources {
  readonly projection: Projection;
  /** The runtimes this host has, in its order. */
  readonly runtimes: () => readonly RuntimeDescriptor[];
}

/** One line of the host's words or someone else's, clipped to fit, never with an ellipsis. */
function clip(text: string, max: number): string {
  const one = text.replace(/\s+/g, ' ').trim();
  return one.length <= max ? one : one.slice(0, max).trimEnd();
}

function row(
  index: number,
  kind: ChoiceRowKind,
  label: string,
  value: string,
  options: { detail?: string | null; theirs?: boolean; actions?: readonly CommandType[] } = {},
): HeldRow {
  return {
    row: {
      index,
      kind,
      label: clip(label, 200),
      detail:
        options.detail === undefined || options.detail === null ? null : clip(options.detail, 400),
      theirs: options.theirs ?? false,
      actions: [...(options.actions ?? [])],
    },
    value,
  };
}

/** A version's reason in the host's words. */
const VERSION_WORDS = {
  before_task: 'Before a Task',
  after_turn: 'After a Turn',
  before_restore: 'Before a Restore',
  before_add: 'Before an Add',
} as const;

/**
 * The rows of one of the host's lists for a scope (ADR 0060, addition B), newest first where they have
 * an age, or null when the list is not one this scope has, or one this host cannot read here yet.
 * The same rows serve the list card, `read_choices` and the preparation of a command from an index.
 */
export function choiceRows(
  list: ChoiceList,
  scope: HereScope,
  sources: ChoiceSources,
): HeldRow[] | null {
  const { projection } = sources;
  const project = scope.project_id === null ? undefined : projection.project(scope.project_id);
  const facts =
    scope.execution_id === null ? undefined : projection.executionFacts(scope.execution_id);
  switch (list) {
    case 'tasks':
      if (project === undefined) return null;
      return projection
        .workstreams(project.project_id)
        .slice()
        .reverse()
        .map((task, index) => row(index, 'task', task.title, task.workstream_id, { theirs: true }));
    case 'versions':
      if (project === undefined) return null;
      return projection
        .checkpoints(project.project_id)
        .slice()
        .sort((a, b) =>
          a.recorded_at < b.recorded_at ? 1 : a.recorded_at > b.recorded_at ? -1 : 0,
        )
        .map((version, index) =>
          row(index, 'version', VERSION_WORDS[version.reason], version.checkpoint_id, {
            detail: version.recorded_at,
            actions: ['project.restore', 'workstream.create'],
          }),
        );
    case 'runtimes':
      return sources
        .runtimes()
        .filter((runtime) => runtime.capabilities.start_execution)
        .map((runtime, index) =>
          row(index, 'runtime', runtime.display_name, runtime.runtime_id, {
            theirs: true,
            actions: ['execution.start'],
          }),
        );
    case 'approval_levels':
      return [
        row(0, 'approval_level', 'Ask First', 'ask'),
        row(1, 'approval_level', 'Never Ask', 'never_ask'),
      ];
    case 'share_ways':
      if (project === undefined) return null;
      return [
        row(0, 'share_way', 'Link', 'link', { actions: ['project.share.start'] }),
        row(1, 'share_way', 'This Wi-Fi', 'wifi', { actions: ['project.share.start'] }),
      ];
    case 'secrets':
      if (project === undefined) return null;
      return (project.secrets ?? []).map((name, index) =>
        row(index, 'secret', name, name, { theirs: true, actions: ['project.secret.remove'] }),
      );
    case 'queued_instructions': {
      if (scope.execution_id === null) return null;
      const waiting = projection.execution(scope.execution_id)?.queued_instructions ?? [];
      return waiting.map((queued, index) =>
        row(index, 'instruction', `Instruction ${index + 1}`, queued.command_id, {
          detail: queued.queued_at,
          actions: ['execution.drop_queued_instruction'],
        }),
      );
    }
    case 'approvals':
      if (facts === undefined) return null;
      return facts.pendingApprovals.map((approval, index) =>
        row(index, 'approval', approval.subject.tool_name, approval.approval_id, {
          detail: approval.subject.summary,
          theirs: true,
          actions: ['execution.respond_to_approval'],
        }),
      );
    case 'decisions':
      if (facts === undefined) return null;
      return [row(0, 'decision', 'Approve', 'approve'), row(1, 'decision', 'Deny', 'deny')];
    case 'questions':
      if (facts === undefined) return null;
      return facts.pendingQuestions.map((question, index) =>
        row(
          index,
          'question',
          question.prompts[0]?.header ?? question.prompts[0]?.text ?? 'Question',
          question.question_id,
          { detail: question.prompts[0]?.text ?? null, theirs: true },
        ),
      );
    // Read through the host's slower reads (a runtime's models, the folder's pictures, the roots, the
    // catalog, the project's scripts): not offered by index yet.
    case 'models':
    case 'pictures':
    case 'folders':
    case 'catalog':
    case 'own_check_scripts':
      return null;
    default: {
      const unhandled: never = list;
      throw new Error(`unhandled list ${String(unhandled)}`);
    }
  }
}

/**
 * The digest of a list as read: each row's kind, words and the value it stands for, in order. A
 * choice given back by index carries it; a list that reads otherwise now refuses the index.
 */
export function choicesDigest(list: ChoiceList, rows: readonly HeldRow[]): string {
  const hash = createHash('sha256').update(list);
  for (const held of rows)
    hash.update(`\0${held.row.kind}\0${held.row.label}\0${held.row.detail ?? ''}\0${held.value}`);
  return hash.digest('hex');
}
