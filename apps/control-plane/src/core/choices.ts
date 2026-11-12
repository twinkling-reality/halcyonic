import { createHash } from 'node:crypto';
import type {
  ChoiceList,
  ChoiceRow,
  ChoiceRowKind,
  CommandType,
  HereScope,
  OwnCheckScript,
  ProjectPicture,
  RuntimeDescriptor,
  RuntimeModel,
} from '@halcyonic/contracts';
import type { Projection } from '@halcyonic/domain';

/** At most this many rows are shown of a list; `total` says how many there are. */
export const CHOICE_ROWS_SHOWN = 32;

/** A row as the host holds it: what the person or the assistant sees, and the value its index stands for. */
export interface HeldRow {
  readonly row: ChoiceRow;
  /** What the payload takes for this row (an id, a word, a picture's reference); never shown, never the model's. */
  readonly value: unknown;
}

export interface ChoiceSources {
  readonly projection: Projection;
  /** The runtimes this host has, in its order. */
  readonly runtimes: () => readonly RuntimeDescriptor[];
  /** The pictures of a project's folder an instruction may carry, as the pictures read finds them. */
  readonly pictures: (folder: string) => Promise<readonly ProjectPicture[]>;
  /** The models a runtime that lists them can use now, from its own list, or none when it cannot say. */
  readonly models: (runtime: RuntimeDescriptor) => Promise<readonly RuntimeModel[]>;
  /** The commands of a project's own that may be its check, as the own check's find reads them, or null. */
  readonly scripts: (projectId: string) => Promise<readonly OwnCheckScript[] | null>;
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
  value: unknown,
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
export async function choiceRows(
  list: ChoiceList,
  scope: HereScope,
  sources: ChoiceSources,
): Promise<HeldRow[] | null> {
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
        row(0, 'approval_level', 'Ask First', 'ask', { actions: ['execution.start'] }),
        row(1, 'approval_level', 'Never Ask', 'never_ask', { actions: ['execution.start'] }),
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
      return [
        row(0, 'decision', 'Approve', 'approve', { actions: ['execution.respond_to_approval'] }),
        row(1, 'decision', 'Deny', 'deny', { actions: ['execution.respond_to_approval'] }),
      ];
    case 'questions':
      if (facts === undefined) return null;
      return facts.pendingQuestions.map((question, index) =>
        row(
          index,
          'question',
          question.prompts[0]?.header ?? question.prompts[0]?.text ?? 'Question',
          question.question_id,
          {
            detail: question.prompts[0]?.text ?? null,
            theirs: true,
            actions: ['execution.answer_question'],
          },
        ),
      );
    case 'pictures': {
      // By path, as the pictures read lists them, each given back as the reference an instruction takes.
      const folder = project?.location?.path;
      if (folder === undefined) return null;
      return (await sources.pictures(folder)).map((picture, index) =>
        row(
          index,
          'picture',
          picture.path,
          { path: picture.path, kind: 'image' },
          {
            detail: picture.modified_at,
            theirs: true,
            actions: ['execution.start', 'execution.send_instruction'],
          },
        ),
      );
    }
    case 'models': {
      // Every model of every runtime that lists its own, each naming its runtime; the start's admission and
      // its runtime check the pair again against a fresh list before anything runs (ADR 0016).
      const rows: HeldRow[] = [];
      for (const runtime of sources.runtimes()) {
        if (runtime.model_choice !== 'listed' || !runtime.capabilities.start_execution) continue;
        for (const model of await sources.models(runtime))
          rows.push(
            row(rows.length, 'model', model.display_name, model.model_ref, {
              detail: runtime.display_name,
              theirs: true,
              actions: ['execution.start'],
            }),
          );
      }
      return rows;
    }
    case 'own_check_scripts': {
      // Each by its name and its command as read now: the choice carries the command's digest, so a
      // command changed since it was shown is refused when the check is chosen (ADR 0055).
      if (project === undefined) return null;
      const scripts = await sources.scripts(project.project_id);
      if (scripts === null) return null;
      return scripts.map((script, index) =>
        row(
          index,
          'script',
          script.name,
          { name: script.name, command_sha256: script.command_sha256 },
          {
            detail: script.command,
            theirs: true,
            actions: ['project.own_check.choose'],
          },
        ),
      );
    }
    // Not offered by index yet: where a new project goes (a folder and its name together), and the catalog.
    case 'folders':
    case 'catalog':
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
    hash.update(
      `\0${held.row.kind}\0${held.row.label}\0${held.row.detail ?? ''}\0${JSON.stringify(held.value)}`,
    );
  return hash.digest('hex');
}
