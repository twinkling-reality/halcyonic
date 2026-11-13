import { realpathSync } from 'node:fs';
import { basename, join } from 'node:path';
import type {
  ActionReview,
  ChoiceList,
  ClientInfo,
  CommandEnvelope,
  CommandId,
  CommandOf,
  HereScope,
  Principal,
  ProjectId,
  WorkstreamId,
} from '@halcyonic/contracts';
import { admitCommand, startOptionsRefusal } from '@halcyonic/domain';
import type { ControlPlane } from './control-plane.ts';
import { answersOwnWork } from './host-answers.ts';
import { cleanWords, type GivenValue } from './prepare.ts';
import { reviewDigest } from './review.ts';
import { saidIn } from './reviews.ts';
import { readRuntimeModels } from './runtime-models.ts';

/**
 * A start (the recap is the review, 2026-11-13): a new project, or a new task in one, and its run, prepared whole
 * before anything is made and shown as one review, then done by one press. Three commands for a new project
 * (`project.create`, `workstream.create`, `execution.start`), two in an existing one, each through its own validator
 * and the whole admission at the press. An id a later command needs from an earlier one's result is not known before
 * the press: it stands as one of these placeholders, never a real id (a v7 id of time zero), and the host fills it
 * from what was made. A proposer never gives an id (R6).
 */
export const START_PROJECT = '00000000-0000-7000-8000-000000000001' as ProjectId;
export const START_TASK = '00000000-0000-7000-8000-000000000002' as WorkstreamId;

/** The values a start takes, by the field of the command each goes to (COMMAND_FIELDS). */
const PROJECT_VALUES = new Set(['name', 'location']);
/** A new project's starter, a row of the `starters` list, which the start puts into its new folder's location. */
const STARTER_VALUE = 'starter';
const RUN_VALUES = new Set([
  'instruction',
  'runtime_id',
  'model_ref',
  'approval_level',
  'plan',
  'spending_cap_usd',
]);

/** A task's title and goal, made by the host from the person's idea, at the fields' own lengths. */
const TITLE_LENGTH = 200;
const GOAL_LENGTH = 4000;

export interface StartReview {
  readonly digest: string;
  readonly confirm_words: string;
  readonly what_happens: string;
  readonly facts: ActionReview['facts'];
  readonly warnings: ActionReview['warnings'];
  readonly said: ActionReview['said'];
}

export type PreparedStart =
  | {
      readonly ok: true;
      /** What the press runs first, in order: a new project, then its task. */
      readonly before: readonly CommandEnvelope[];
      /** The run's own start, the step's command. */
      readonly command: CommandEnvelope;
      readonly review: StartReview;
    }
  | {
      readonly ok: false;
      readonly code: string;
      readonly message: string;
      /** The list with no default row, where none was chosen from it. */
      readonly needs?: ChoiceList;
    };

const refuse = (code: string, message: string, needs?: ChoiceList): PreparedStart => ({
  ok: false,
  code,
  message,
  ...(needs === undefined ? {} : { needs }),
});

/**
 * Prepares a start from the person's press (`act` with `execution.start` where no task is bound): each value by its
 * command's own field, each choice an index into a host list as read, and every value not given taken from that
 * list's `default`. Nothing is made here: no project, folder, task, copy or runtime is touched until the press.
 */
export async function prepareStart(
  plane: ControlPlane,
  input: {
    readonly scope: HereScope;
    readonly values: Readonly<Record<string, GivenValue>>;
    readonly lists: Readonly<Record<string, string>>;
    readonly id: () => CommandId;
    readonly now: string;
    readonly client: ClientInfo;
  },
): Promise<PreparedStart> {
  const { scope } = input;
  if (scope.workstream_id !== null || scope.execution_id !== null)
    return refuse('invalid_state', 'A start makes a new task; this request already has one.');
  const newProject = scope.project_id === null;
  const project = newProject ? undefined : plane.projection.project(scope.project_id as string);
  if (!newProject && project === undefined)
    return refuse('invalid_state', 'That project is not here any more.');
  for (const name of Object.keys(input.values))
    if (
      !RUN_VALUES.has(name) &&
      !(newProject && (PROJECT_VALUES.has(name) || name === STARTER_VALUE))
    )
      return refuse('invalid_state', `${name} is not given here.`);
  const idea = input.values.instruction;
  const title = typeof idea === 'string' ? cleanWords(idea, true, TITLE_LENGTH) : '';
  if (title === '') return refuse('invalid_state', 'Instruction needs words.');
  const values: Record<string, GivenValue> = { ...input.values };
  const read: Record<string, string> = { ...input.lists };
  // Every choice not given is the list's default, by the host's own facts, with the list's digest as read now.
  const take = async (
    field: string,
    list: ChoiceList,
    runtimeId: string | null = null,
  ): Promise<boolean> => {
    if (values[field] !== undefined) return true;
    const taken = await plane.commands.defaultOf(scope, list, runtimeId);
    if (taken === null) return false;
    values[field] = taken.index;
    read[list] = taken.digest;
    return true;
  };
  if (!(await take('runtime_id', 'runtimes')))
    return refuse('choice_needed', 'Choose the agent app to start with.', 'runtimes');
  const runtimeId = await plane.commands.rowValue(
    scope,
    'runtimes',
    values.runtime_id as number,
    read.runtimes,
  );
  const runtime = typeof runtimeId === 'string' ? plane.registry.get(runtimeId) : undefined;
  if (runtime === undefined)
    return refuse('choices_changed', 'The agent app list changed. Read it again.');
  if (runtime.model_choice === 'listed' && !(await take('model_ref', 'models', runtime.runtime_id)))
    return refuse('choice_needed', `Choose the model ${runtime.display_name} runs.`, 'models');
  if (newProject) {
    await take('location', 'folders');
    if (values.location === undefined && runtime.uses_project_location)
      return refuse(
        'choice_needed',
        `Choose the folder ${runtime.display_name} works in.`,
        'folders',
      );
  }
  const ours = (names: ReadonlySet<string>) =>
    Object.fromEntries(Object.entries(values).filter(([name]) => names.has(name)));
  const prepare = (
    command_type: string,
    bound: HereScope,
    given: Record<string, GivenValue>,
    lists: Record<string, string>,
  ) =>
    plane.commands.prepareInStart(
      {
        scope: bound,
        command_type,
        values: given,
        read: lists,
        command_id: input.id(),
        issued_at: input.now,
        client: input.client,
      },
      scope,
    );
  const admitted = (command: CommandEnvelope) => {
    const admission = admitCommand(command, plane.projection, {
      get: (id) => plane.registry.get(id),
      queuesInstructions: plane.journal.info.origin === 'live',
    });
    return admission.admitted ? null : admission.rejection;
  };
  const before: CommandEnvelope[] = [];
  if (newProject) {
    const made = await prepare(
      'project.create',
      { project_id: null, workstream_id: null, execution_id: null },
      ours(PROJECT_VALUES),
      read.folders === undefined ? {} : { folders: read.folders },
    );
    if (!made.ok) return refuse(made.code, made.message);
    let create = made.command as CommandOf<'project.create'>;
    // A starter is a row of the host's `starters` list, never an id given (R6), with no default: a new project
    // begins from one only where the person picked it. It goes into the new folder project.create makes.
    const picked = values[STARTER_VALUE];
    if (picked !== undefined) {
      const starter =
        typeof picked === 'number'
          ? await plane.commands.rowValue(scope, 'starters', picked, read.starters)
          : undefined;
      if (typeof starter !== 'string')
        return refuse('choices_changed', 'The starter list changed. Read it again.');
      const location = create.payload.location;
      if (location?.kind !== 'new_folder')
        return refuse('invalid_state', 'A starter begins only in a new folder.');
      const ready = plane.starters?.check(starter) ?? {
        ok: false as const,
        message: 'This computer has no starters.',
      };
      if (!ready.ok) return refuse('capability_unsupported', ready.message);
      create = {
        ...create,
        payload: {
          ...create.payload,
          location: {
            kind: 'new_from_starter',
            root: location.root,
            folder_name: location.folder_name,
            starter_id: starter as never,
          },
        },
      };
    }
    const refused = admitted(create);
    if (refused !== null) return refuse(refused.code, refused.message);
    before.push(create);
  }
  const projectId = newProject ? START_PROJECT : (scope.project_id as ProjectId);
  const task = await prepare(
    'workstream.create',
    { project_id: projectId, workstream_id: null, execution_id: null },
    {
      title,
      objective: cleanWords(idea as string, false, GOAL_LENGTH),
    },
    {},
  );
  if (!task.ok) return refuse(task.code, task.message);
  if (!newProject) {
    const refused = admitted(task.command);
    if (refused !== null) return refuse(refused.code, refused.message);
  }
  before.push(task.command);
  const run = await prepare(
    'execution.start',
    { project_id: projectId, workstream_id: START_TASK, execution_id: null },
    ours(RUN_VALUES),
    Object.fromEntries(Object.entries(read).filter(([list]) => list !== 'folders')) as Record<
      string,
      string
    >,
  );
  if (!run.ok) return refuse(run.code, run.message);
  // What the runtime alone refuses, and a folder it needs, are known now: never found only after a project is made.
  const start = run.command as CommandOf<'execution.start'>;
  const options = startOptionsRefusal(runtime, start);
  if (options !== null) return refuse(options.code, options.message);
  const folder = newProject
    ? (before[0] as CommandOf<'project.create'>).payload.location !== null
    : project?.location !== null && project?.location !== undefined;
  if (runtime.uses_project_location && !folder)
    return refuse('location_required', `${runtime.display_name} needs a folder to work in.`);
  const checked = plane.registry
    .adapter(runtime.runtime_id)
    ?.validateStartOptions(
      start.payload.options,
      start.payload.model_ref,
      start.payload.approval_level,
    );
  if (checked !== undefined && !checked.ok)
    return refuse('invalid_runtime_options', checked.message);
  const review = await startReview(plane, before, run.command);
  if (!review.ok) return refuse(review.code, review.message);
  return { ok: true, before, command: run.command, review: review.review };
}

/**
 * The one review of a start, from the commands held and what the host knows now, so the press recomputes it and a
 * difference answers review_stale: what happens; at most four facts (the agent app with its model, where it runs,
 * the folder, what it may do without asking); the warnings that say what it costs by the route it takes (M3), where
 * it runs outside the sandbox, never asking, and a spending limit; and the person's own words, whole.
 */
export async function startReview(
  plane: ControlPlane,
  before: readonly CommandEnvelope[],
  command: CommandEnvelope,
): Promise<{ ok: true; review: StartReview } | { ok: false; code: string; message: string }> {
  if (command.command_type !== 'execution.start')
    return { ok: false, code: 'invalid_state', message: 'That is not a start.' };
  const run = command.payload;
  const runtime = plane.registry.get(run.runtime_id);
  if (runtime === undefined)
    return { ok: false, code: 'runtime_unavailable', message: 'That agent app is not here now.' };
  const app = runtime.display_name;
  // The model as the app lists it now; a list that cannot be read leaves where it runs unknown.
  const adapter = plane.registry.adapter(runtime.runtime_id);
  const listed =
    run.model_ref === null || adapter === undefined
      ? null
      : await readRuntimeModels(adapter, undefined, plane.redaction);
  const model =
    listed?.availability === 'available'
      ? listed.models.find((each) => each.model_ref === run.model_ref)
      : undefined;
  const served = model?.served ?? 'unknown';
  const provider =
    model?.provider === undefined
      ? null
      : (plane.providers?.byId(model.provider)?.name ?? model.provider);
  // A key the person added pays where the model goes through a declared provider whose key is stored here.
  const keyAdded =
    model?.provider !== undefined && plane.providers?.keyState(model.provider).kind === 'stored';
  const created = before.find(
    (each): each is CommandOf<'project.create'> => each.command_type === 'project.create',
  );
  const project =
    created === undefined
      ? plane.projection.project(
          (
            before.find((each) => each.command_type === 'workstream.create') as
              | CommandOf<'workstream.create'>
              | undefined
          )?.payload.project_id ?? '',
        )
      : undefined;
  const location = created?.payload.location ?? null;
  // A starter by the title it declares, as the starters list shows it; its id where it no longer says one.
  const starter =
    location?.kind === 'new_from_starter'
      ? (plane.starters?.list().find((each) => each.starter_id === location.starter_id)?.title ??
        location.starter_id)
      : null;
  const folder =
    created === undefined
      ? project?.location
        ? basename(project.location.path)
        : 'No Folder'
      : location === null
        ? 'No Folder'
        : location.kind === 'new_folder'
          ? `New Folder ${location.folder_name}`
          : location.kind === 'existing_folder'
            ? (location.folder_name ?? basename(location.root))
            : `New Folder ${location.folder_name} From ${starter}`;
  // The folder the run will work in, and whether it will be a starter's project, as the press will make it.
  const workIn =
    created === undefined
      ? (project?.location?.path ?? null)
      : location === null
        ? null
        : location.kind === 'existing_folder'
          ? location.folder_name === null
            ? location.root
            : join(location.root, location.folder_name)
          : join(location.root, location.folder_name);
  const starterProject = location?.kind === 'new_from_starter';
  // What the run will really ask, from its adapter where the runtime can run at another level than the one asked for
  // (Codex asks nothing on a starter's confined project, ADR 0041): the same choice its start makes, never a copy here.
  const really =
    workIn === null
      ? null
      : (adapter?.levelAt?.({
          directory: workIn,
          starter_project: starterProject,
          options: run.options,
          model_ref: run.model_ref,
          ...(run.approval_level === undefined || run.approval_level === null
            ? {}
            : { approval_level: run.approval_level }),
        }) ?? null);
  const asks =
    really !== null
      ? really === 'ask'
      : run.approval_level === 'never_ask'
        ? false
        : run.approval_level === 'ask'
          ? true
          : runtime.capabilities.respond_to_approval;
  // A confined app's own work goes ahead without a card (the no-ask unit): where the host answers it, the run asks
  // only for what reaches outside its folder. The same host fact the no-ask unit reads: the runtime kept inside the
  // sandbox, a kind the host answers for, in a starter's project (a new folder begun from one, or the project's own).
  const ownWorkAnswered =
    asks &&
    answersOwnWork(runtime.kind) &&
    !plane.registry.outsideSandbox().some((each) => each.runtime_id === runtime.runtime_id) &&
    plane.answersOwnWorkIn(created === undefined ? workIn : null, starterProject);
  // An adapter that can say the level but not for these bounds leaves it unknown: the card never guesses (AUDIT2 S1).
  const levelUnknown = adapter?.levelAt !== undefined && really === null;
  const level = levelUnknown
    ? 'Not Known'
    : !asks
      ? 'Works Without Asking'
      : ownWorkAnswered
        ? 'Asks Only Outside Task Folder'
        : 'Asks First';
  const facts: ActionReview['facts'] = [
    {
      kind: 'agent',
      value: model === undefined ? app : `${app} with ${model.display_name}`,
      theirs: true,
    },
    {
      kind: 'place',
      value:
        served === 'this_mac'
          ? 'This Computer'
          : served === 'remote'
            ? (provider ?? 'Online')
            : 'Not Known',
      theirs: served === 'remote' && provider !== null,
    },
    { kind: 'folder', value: folder, theirs: folder !== 'No Folder' },
    { kind: 'level', value: level, theirs: false },
  ];
  // Short enough to read at a glance at the larger text size, who pays said once, and no product name on the card
  // (AUDIT2, 16:47Z, after the owner's report on the long cost lines), then held to a fresh reader's changes. A run that
  // never asks says so first, in words of its own: the level fact alone was misread by a fresh reader (21:3xZ), and
  // the card's one promise about consent must not ride on it (AUDIT2, 21:26Z).
  const warnings: ActionReview['warnings'][number][] = [];
  if (!asks && !levelUnknown)
    warnings.push({
      code: 'never_asks',
      line: 'Runs commands and changes files without asking you',
    });
  if (served === 'this_mac')
    warnings.push({ code: 'free_here', line: 'Free: the model runs on this computer' });
  else if (served === 'remote') {
    warnings.push(
      keyAdded && provider !== null
        ? { code: 'costs_money', line: `It costs money on your own ${provider} account` }
        : { code: 'costs_money', line: `Any cost is on your own ${app} account` },
      // No descriptor fact says which apps price their runs (Codex reports usage and never a cost), so "where it gives one".
      {
        code: 'price_unknown',
        line: "The price is not known; the app's estimate shows when it finishes, where it gives one",
      },
      {
        code: 'sent_to_provider',
        line:
          provider === null
            ? 'Your idea, and what the agent reads in the folder, leave this computer'
            : `Your idea, and what the agent reads in the folder, go to ${provider}`,
      },
    );
  } else
    warnings.push(
      {
        code: 'cost_unknown',
        // The account the app uses: its own sign-in, or a key the person added; which is not known here.
        line: `Any cost is on the account ${app} uses; the final price is not known`,
      },
      // Where it runs is not known, so neither is whether the idea stays here: said as may, never as does.
      { code: 'may_leave', line: 'Your idea, and what the agent reads, may leave this computer' },
    );
  if (plane.registry.outsideSandbox().some((each) => each.runtime_id === runtime.runtime_id))
    warnings.push({ code: 'unconfined', line: 'It can reach files beyond its folder' });
  if (run.spending_cap_usd !== undefined)
    warnings.push({
      code: 'spending_limit',
      line: `It stops near $${run.spending_cap_usd.toFixed(2)} by the app's own estimate; it can go over on its last step`,
    });
  // An existing folder whose project was taken off the list brings that project back, with its own name (ADR 0063):
  // the card says so, never that it makes one, and the name typed is not said, since it is not used.
  const back = location?.kind === 'existing_folder' ? returning(plane, location) : null;
  const said = [
    ...(created === undefined || back !== null ? [] : saidIn(created)),
    ...saidIn(command),
  ];
  const what_happens =
    created === undefined
      ? 'Makes a new task in this project and starts the agent working on your idea'
      : back !== null
        ? `Brings back ${back}, then starts the agent working on your idea`
        : starter !== null
          ? `Makes the project and its new folder from ${starter}, then starts the agent working on your idea`
          : location?.kind === 'new_folder'
            ? 'Makes the project and its new folder, then starts the agent working on your idea'
            : 'Makes the project, then starts the agent working on your idea';
  const shown = { what_happens, facts, warnings, said };
  return {
    ok: true,
    review: {
      confirm_words: 'Start Building',
      ...shown,
      digest: reviewDigest(
        'execution.start',
        {
          before: before.map((each) => ({
            command_type: each.command_type,
            payload: each.payload,
          })),
          command: run,
        },
        shown,
      ),
    },
  };
}

/** What a start's press made, and where it stopped, if it did. */
export interface StartRun {
  readonly made: HereScope;
  readonly stopped: null | {
    readonly command_type: string;
    readonly code: string;
    readonly message: string;
  };
}

/**
 * The press: each held command in order under the person's own principal, each through the whole admission, the
 * placeholders filled from what the one before made. It stops at the first refused or failed command; what ran
 * stays made and is said, never folded into nothing changed. `progress` is told each time something is made.
 */
export function runStart(
  plane: ControlPlane,
  before: readonly CommandEnvelope[],
  command: CommandEnvelope,
  principal: Principal,
  progress: (made: HereScope) => void,
): StartRun {
  let made: HereScope = {
    project_id: null,
    workstream_id: null,
    execution_id: null,
  };
  const fill = (held: CommandEnvelope): CommandEnvelope => {
    const payload = { ...(held.payload as Record<string, unknown>) };
    if (payload.project_id === START_PROJECT) payload.project_id = made.project_id;
    if (payload.workstream_id === START_TASK) payload.workstream_id = made.workstream_id;
    return { ...held, payload } as CommandEnvelope;
  };
  const submit = (held: CommandEnvelope) => {
    const outcome = plane.commands.submit(fill(held), 'internal', principal);
    const view = plane.projection.command(held.command_id);
    const why = view?.rejection ?? view?.failure ?? null;
    return {
      view,
      stopped:
        outcome.disposition === 'rejected' || why !== null
          ? {
              command_type: held.command_type,
              code: why?.code ?? 'unknown',
              message: why?.message ?? 'It was refused.',
            }
          : null,
    };
  };
  for (const held of before) {
    if (held.command_type === 'workstream.create') {
      const bound = (held as CommandOf<'workstream.create'>).payload.project_id;
      if (bound !== START_PROJECT) made = { ...made, project_id: bound };
    }
    const { view, stopped } = submit(held);
    if (stopped !== null) return { made, stopped };
    const result = view?.result ?? null;
    if (result?.kind === 'project_created') made = { ...made, project_id: result.project_id };
    else if (result?.kind === 'workstream_created')
      made = { ...made, workstream_id: result.workstream_id };
    else
      return {
        made,
        stopped: {
          command_type: held.command_type,
          code: 'unknown',
          message: 'Its outcome is not known.',
        },
      };
    progress(made);
  }
  const { stopped } = submit(command);
  if (stopped !== null) return { made, stopped };
  const execution =
    made.workstream_id === null
      ? null
      : (plane.projection.workstream(made.workstream_id)?.current_execution_id ?? null);
  made = { ...made, execution_id: execution };
  progress(made);
  return { made, stopped: null };
}

/**
 * The name of the project taken off the list whose folder `location` is, the one taken off last, as project.create
 * will bring it back (ADR 0063); null where none was, or the folder cannot be read now.
 */
function returning(
  plane: ControlPlane,
  location: { readonly root: string; readonly folder_name: string | null },
): string | null {
  let path: string;
  try {
    path = realpathSync(
      location.folder_name === null ? location.root : join(location.root, location.folder_name),
    );
  } catch {
    return null;
  }
  const off = plane.projection
    .projects()
    .filter((project) => project.removed_at !== undefined && project.location?.path === path)
    .sort((a, b) => (b.removed_at ?? '').localeCompare(a.removed_at ?? ''))[0];
  return off?.name ?? null;
}
