import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import {
  mkdirSync,
  mkdtempSync,
  readdirSync,
  readFileSync,
  realpathSync,
  rmSync,
  writeFileSync,
} from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';
import type {
  CommandId,
  ExecutionView,
  ProjectId,
  RuntimeId,
  RuntimeModel,
  WorkspaceRequestStep,
} from '@halcyonic/contracts';
import type { Projection } from '@halcyonic/domain';
import type { RuntimeAdapter } from '@halcyonic/runtime-core';
import { defaultRow, type HeldRow } from '../core/choices.ts';
import { removalReview } from '../core/removal.ts';
import { START_PROJECT } from '../core/start.ts';
import { createUuidV7Generator } from '../ids.ts';
import { createHostLocations } from '../locations.ts';
import { HostProviders } from '../providers.ts';
import { HostStarters } from '../starters.ts';
import { createTestControlPlane } from '../testing/harness.ts';
import { WorkspaceRequests } from './requests.ts';

const settle = async () => {
  for (let i = 0; i < 32; i++) await new Promise<void>((resolve) => setImmediate(resolve));
};
const unavailable = {
  availability: 'unavailable' as const,
  reason: { code: 'synthetic_unavailable', message: 'No provider in this test.' },
};

/** An agent app for these tests: real or the practice run by its declared kind, listing `models` where it lists any. */
function app(
  id: string,
  options: {
    synthetic?: boolean;
    models?: () => readonly RuntimeModel[];
    folder?: boolean;
    kind?: string;
    outside?: boolean;
    /** What the adapter says a start will really run at, where it says (`levelAt`). */
    levelAt?: () => 'ask' | 'never_ask' | null;
    startsOk?: () => boolean;
  } = {},
): RuntimeAdapter {
  return {
    descriptor: {
      runtime_id: id as RuntimeId,
      kind: (options.kind ?? 'stub') as never,
      display_name: id === 'practice' ? 'Practice' : `App ${id}`,
      synthetic: options.synthetic ?? false,
      capabilities: {
        start_execution: true,
        instruct_at_rest: true,
        instruct_while_running: false,
        respond_to_approval: true,
        answer_question: false,
        interrupt: false,
      },
      model_choice: options.models === undefined ? 'none' : 'listed',
      uses_project_location: options.folder ?? false,
      reports_tool_activity: false,
    },
    validateStartOptions: () =>
      (options.startsOk?.() ?? true) ? { ok: true } : { ok: false, message: 'Not now.' },
    ...(options.models === undefined
      ? {}
      : { listModels: async () => (options.models as () => readonly RuntimeModel[])() }),
    // Kept to its folder, so no unconfined line is said for it.
    runsOutsideSandbox: () => options.outside === true,
    ...(options.levelAt === undefined ? {} : { levelAt: options.levelAt }),
    startExecution: async () => ({ native_id: `native-${id}` }),
    sendInstruction: async () => {},
    respondToApproval: async () => {},
    close: async () => {},
  } as RuntimeAdapter;
}

const HERE: RuntimeModel = {
  model_ref: 'here/a',
  display_name: 'Here A',
  served: 'this_mac',
  tool_calling: 'declared',
  context_tokens: null,
};

function setup(
  t: { after: (fn: () => unknown) => void },
  adapters: readonly RuntimeAdapter[],
  extra: {
    providers?: HostProviders;
    starters?: HostStarters;
    confinedReads?: (directory: string) => { readable: string[] } | null;
  } = {},
) {
  const root = realpathSync(mkdtempSync(join(tmpdir(), 'halcyonic-start-')));
  t.after(() => rmSync(root, { recursive: true, force: true }));
  const h = createTestControlPlane({
    adapters: () => [...adapters],
    locations: createHostLocations([root]),
    ...extra,
  });
  const ids = createUuidV7Generator({ now: () => h.time.now().getTime() });
  const requests = new WorkspaceRequests({
    controlPlane: h.controlPlane,
    ids,
    model: {
      status: async () => unavailable,
      turn: async () => {
        throw new Error('No model is asked for a press.');
      },
    },
    understanding: { understand: async () => unavailable },
    evaluation: { evaluate: async () => unavailable },
    scheduler: h.time,
  });
  t.after(async () => {
    requests.close();
    await h.controlPlane.close();
  });
  const envelope = () => ({
    schema_version: 1 as const,
    command_id: ids.next() as CommandId,
    issued_at: h.time.now().toISOString(),
    client: { name: 'headset', version: null, device_label: null },
  });
  const press = async (
    values: Record<string, unknown>,
    lists: Record<string, string> = {},
    projectId: string | null = null,
  ) => {
    const outcome = h.controlPlane.commands.submit(
      {
        ...envelope(),
        command_type: 'workspace.request.submit',
        payload: {
          project_id: projectId as ProjectId | null,
          workstream_id: null,
          execution_id: null,
          text: 'Start Building',
          act: { command_type: 'execution.start', values: values as never, lists },
        },
      },
      'http',
      { kind: 'local' },
    );
    assert.equal(outcome.command?.result?.kind, 'workspace_request_created');
    const id =
      outcome.command?.result?.kind === 'workspace_request_created'
        ? outcome.command.result.request_id
        : '';
    await settle();
    const step = h.controlPlane.projection
      .workspaceRequest(id)
      ?.steps.find((each) => each.tool_name === 'act') as WorkspaceRequestStep;
    return { id, step };
  };
  const confirm = (id: string, step: WorkspaceRequestStep, review = step.proposal?.review ?? '') =>
    h.controlPlane.commands.submit(
      {
        ...envelope(),
        command_type: 'workspace.request.resume',
        payload: {
          request_id: id as never,
          text: null,
          confirm: { step_id: step.step_id, review },
        },
      },
      'http',
      { kind: 'local' },
    );
  const cancel = (id: string) =>
    h.controlPlane.commands.submit(
      {
        ...envelope(),
        command_type: 'workspace.request.cancel',
        payload: { request_id: id as never },
      },
      'http',
      { kind: 'local' },
    );
  const step = (id: string, stepId: string) =>
    h.controlPlane.projection
      .workspaceRequest(id)
      ?.steps.find((each) => each.step_id === stepId) as WorkspaceRequestStep;
  const made = () => ({
    projects: h.controlPlane.projection.projects().length,
    folders: readdirSync(root).length,
    events: h.controlPlane.projection.executions().map((run) => run.execution_id),
  });
  return { h, root, press, confirm, cancel, step, made };
}

test('a start is prepared whole as one review and makes nothing before the press; a cancel leaves nothing (AUDIT 1)', async (t) => {
  const s = setup(t, [
    app('practice', { synthetic: true }),
    app('real', { models: () => [HERE], folder: true }),
  ]);
  const before = s.made();
  const { id, step } = await s.press({
    instruction: 'A page of race times for my running club',
    name: 'Race Times',
  });
  assert.equal(step.status, 'waiting', step.summary);
  const proposal = step.proposal;
  assert.ok(proposal);
  assert.equal(proposal.command_type, 'execution.start');
  assert.equal(proposal.confirm_words, 'Start Building');
  assert.equal(
    proposal.what_happens,
    'Makes the project and its new folder, then starts the agent working on your idea',
  );
  // Every default the host took is a fact on the card: the real app (never the practice run), its one model, the
  // new folder named from the project's name, and the level.
  assert.deepEqual(proposal.facts, [
    { kind: 'agent', value: 'App real with Here A', theirs: true },
    { kind: 'place', value: 'This Computer', theirs: false },
    { kind: 'folder', value: 'New Folder Race-Times', theirs: true },
    { kind: 'level', value: 'Asks First', theirs: false },
  ]);
  assert.deepEqual(
    proposal.warnings?.map((warning) => warning.code),
    ['free_here'],
  );
  // The person's words whole, as they go out.
  assert.deepEqual(proposal.said, [
    { label: 'Name', words: 'Race Times' },
    { label: 'Instruction', words: 'A page of race times for my running club' },
  ]);
  // The commands are held, the task's project a placeholder; nothing is made.
  const held = s.h.controlPlane.projection.workspaceProposal(id, step.step_id);
  assert.deepEqual(
    held?.before?.map((command) => command.command_type),
    ['project.create', 'workstream.create'],
  );
  const task = held?.before?.[1];
  assert.ok(task);
  assert.equal((task.payload as { project_id: string }).project_id, START_PROJECT);
  assert.equal(step.command_id, held?.command.command_id);
  assert.deepEqual(s.made(), before);
  s.cancel(id);
  await settle();
  assert.equal(s.step(id, step.step_id).status, 'rejected');
  assert.deepEqual(s.made(), before, 'a cancel leaves no project, task, run or folder');
});

test('the press makes the project, its folder and task and starts the run, saying each; a second press makes nothing (M2)', async (t) => {
  const s = setup(t, [app('real', { models: () => [HERE], folder: true })]);
  const before = s.made();
  const { id, step } = await s.press({ instruction: 'Tide tables', name: 'Tides' });
  assert.equal(step.status, 'waiting', step.summary);
  // A digest not the one shown is refused at once.
  assert.equal(s.confirm(id, step, 'c'.repeat(64)).command?.failure?.code, 'review_stale');
  const pressed = s.confirm(id, step);
  assert.equal(pressed.disposition, 'accepted', JSON.stringify(pressed.command?.rejection));
  // A second press while the first runs is refused in words.
  assert.equal(s.confirm(id, step).command?.failure?.code, 'invalid_state');
  await settle();
  const after = s.step(id, step.step_id);
  assert.notEqual(after.status, 'rejected', after.summary);
  assert.equal(after.command_id, step.command_id, "the step's command stays the run's start");
  const made = after.made;
  assert.ok(made?.project_id && made.workstream_id && made.execution_id);
  const project = s.h.controlPlane.projection.project(made.project_id);
  assert.equal(project?.name, 'Tides');
  assert.equal(project?.location?.path, join(s.root, 'Tides'));
  const task = s.h.controlPlane.projection.workstream(made.workstream_id);
  assert.equal(task?.title, 'Tide tables');
  assert.equal(task?.current_execution_id, made.execution_id);
  assert.equal(s.made().projects, before.projects + 1);
  // Pressed again once done: nothing waits, never a second project.
  assert.equal(s.confirm(id, after).command?.failure?.code, 'invalid_state');
  await settle();
  assert.equal(s.made().projects, before.projects + 1);
});

test('a start that stops part way says what it made, and its project is never made twice (AUDIT 5, M2)', async (t) => {
  let startsOk = true;
  const s = setup(t, [app('real', { models: () => [HERE], startsOk: () => startsOk })]);
  const before = s.made();
  const { id, step } = await s.press({ instruction: 'Night sky', name: 'Sky' });
  assert.equal(step.status, 'waiting', step.summary);
  startsOk = false;
  s.confirm(id, step);
  await settle();
  const after = s.step(id, step.step_id);
  assert.equal(after.status, 'rejected');
  assert.ok(after.made?.project_id && after.made.workstream_id);
  assert.equal(after.made.execution_id, null);
  assert.equal(
    after.summary,
    'The project and its task were made, but the agent did not start: Not now.',
  );
  assert.equal(s.made().projects, before.projects + 1);
  assert.equal(s.confirm(id, after).command?.failure?.code, 'invalid_state');
  await settle();
  assert.equal(s.made().projects, before.projects + 1, 'no second project');
});

test("a start's ids are the host's: a value naming a project or a task is refused, and nothing is made (M1)", async (t) => {
  const s = setup(t, [app('real', { models: () => [HERE] })]);
  const before = s.made();
  for (const values of [
    { instruction: 'x', name: 'X', project_id: START_PROJECT },
    { instruction: 'x', name: 'X', workstream_id: START_PROJECT },
  ]) {
    const { step } = await s.press(values);
    assert.equal(step.status, 'rejected');
    assert.match(step.summary, /is not given here/);
  }
  assert.deepEqual(s.made(), before);
});

test('what was shown is made again at the press: a model that changed since makes nothing and says so', async (t) => {
  let name = 'Here A';
  const s = setup(t, [app('real', { models: () => [{ ...HERE, display_name: name }] })]);
  const before = s.made();
  const { id, step } = await s.press({ instruction: 'Garden', name: 'Garden' });
  assert.equal(step.status, 'waiting', step.summary);
  name = 'Here A Two';
  assert.equal(s.confirm(id, step).disposition, 'accepted');
  await settle();
  const after = s.step(id, step.step_id);
  assert.equal(after.status, 'rejected');
  assert.match(after.summary, /nothing was made/);
  assert.deepEqual(s.made(), before);
});

test('with no default the start names the list to choose from, and makes nothing; the practice run is never one', async (t) => {
  const onlyPractice = setup(t, [app('practice', { synthetic: true })]);
  const first = await onlyPractice.press({ instruction: 'x', name: 'X' });
  assert.equal(first.step.status, 'rejected');
  assert.equal(first.step.needs_choice, 'runtimes');
  const twoModels = setup(t, [
    app('real', { models: () => [HERE, { ...HERE, model_ref: 'here/b', display_name: 'Here B' }] }),
  ]);
  const second = await twoModels.press({ instruction: 'x', name: 'X' });
  assert.equal(second.step.needs_choice, 'models');
  assert.equal(twoModels.made().projects, 0);
});

test('an existing project takes a new task and its run, two commands, its own folder named', async (t) => {
  const s = setup(t, [app('real', { models: () => [HERE] })]);
  const created = s.h.controlPlane.commands.submit(s.h.commands.createProject('Kept'), 'internal');
  const projectId =
    created.command?.result?.kind === 'project_created' ? created.command.result.project_id : '';
  const { id, step } = await s.press({ instruction: 'Add a map' }, {}, projectId);
  assert.equal(step.status, 'waiting', step.summary);
  assert.equal(
    step.proposal?.what_happens,
    'Makes a new task in this project and starts the agent working on your idea',
  );
  assert.equal(step.proposal?.facts?.[2]?.value, 'No Folder');
  // A project's name is not a start's to give here.
  const named = await s.press({ instruction: 'Add a map', name: 'Other' }, {}, projectId);
  assert.equal(named.step.status, 'rejected');
  s.confirm(id, step);
  await settle();
  const after = s.step(id, step.step_id);
  assert.equal(after.made?.project_id, projectId);
  assert.equal(s.h.controlPlane.projection.workstreams(projectId).length, 1);
});

test('the cost words follow the route the run takes: a key the person added, the app on its own, this computer', async (t) => {
  const dir = mkdtempSync(join(tmpdir(), 'halcyonic-start-providers-'));
  t.after(() => rmSync(dir, { recursive: true, force: true }));
  mkdirSync(join(dir, 'alpha'));
  writeFileSync(
    join(dir, 'alpha', 'provider.json'),
    JSON.stringify({
      format: 1,
      id: 'alpha',
      name: 'Alpha',
      upstream: 'http://127.0.0.1:9/v1',
      speaks: ['chat_completions'],
      key: {
        file: 'alpha-api-key',
        variable: 'ALPHA_API_KEY',
        field: 'Alpha API Key',
        header: { name: 'Authorization', scheme: 'Bearer' },
      },
      models: ['one'],
    }),
  );
  const far: RuntimeModel = {
    ...HERE,
    model_ref: 'alpha/one',
    display_name: 'One',
    served: 'remote',
    provider: 'alpha',
  };
  const words = async (keyed: boolean) => {
    if (keyed) writeFileSync(join(dir, 'alpha-api-key'), 'not-a-real-key\n', { mode: 0o600 });
    else rmSync(join(dir, 'alpha-api-key'), { force: true });
    const s = setup(t, [app('real', { models: () => [far] })], {
      providers: new HostProviders({ dir, dataDir: dir }),
    });
    const { step } = await s.press({ instruction: 'x', name: 'X' });
    assert.equal(step.status, 'waiting', step.summary);
    return step.proposal;
  };
  const keyed = await words(true);
  assert.equal(keyed?.facts?.[1]?.value, 'Alpha');
  assert.equal(keyed?.warnings?.[0]?.line, 'It costs money on your own Alpha account');
  const own = await words(false);
  assert.equal(own?.warnings?.[0]?.line, 'Any cost is on your own App real account');
  // No product name anywhere on the card, on either route.
  for (const card of [keyed, own])
    assert.ok(!JSON.stringify(card).includes('Halcyonic'), 'the card names no product');
  assert.deepEqual(
    own?.warnings?.map((warning) => warning.code),
    ['costs_money', 'price_unknown', 'sent_to_provider'],
  );
  // This app reports no cost at all, so the card never promises an estimate: it shows only where the app gives one.
  for (const card of [keyed, own])
    assert.equal(
      card?.warnings?.find((warning) => warning.code === 'price_unknown')?.line,
      "The price is not known; the app's estimate shows when it finishes, where it gives one",
    );
});

test("the host's default rows come from its own facts: a real app only, the last real run's app and model, a new folder", () => {
  const held = (
    index: number,
    value: unknown,
    actions: string[] = ['execution.start'],
  ): HeldRow => ({
    row: {
      index,
      kind: 'runtime',
      label: String(index),
      detail: null,
      theirs: true,
      actions: actions as never,
    },
    value,
  });
  const run = (runtime: string, model: string | null, synthetic = false, project = 'p') =>
    ({
      runtime: { runtime_id: runtime, synthetic },
      model_ref: model,
      project_id: project,
    }) as unknown as ExecutionView;
  const sources = (runs: ExecutionView[], synthetic: string[] = ['practice']) => ({
    projection: { executions: () => runs } as unknown as Projection,
    runtimes: () =>
      ['practice', 'a', 'b'].map((id) => ({
        runtime_id: id,
        synthetic: synthetic.includes(id),
      })) as never,
  });
  const scope = { project_id: 'p' as ProjectId, workstream_id: null, execution_id: null };
  const apps = [held(0, 'practice'), held(1, 'a'), held(2, 'b')];
  // Several real apps, none used before: no default.
  assert.equal(defaultRow('runtimes', scope, apps, sources([]), null), null);
  // The practice run used last never seeds the default, nor is the default as the only one ready.
  assert.equal(
    defaultRow('runtimes', scope, apps, sources([run('practice', null, true)]), null),
    null,
  );
  assert.equal(defaultRow('runtimes', scope, [held(0, 'practice')], sources([]), null), null);
  // Exactly one real app ready: it.
  assert.equal(
    defaultRow('runtimes', scope, [held(0, 'practice'), held(1, 'a')], sources([]), null),
    1,
  );
  // The last real run's app wins, this project's before another's.
  assert.equal(
    defaultRow(
      'runtimes',
      scope,
      apps,
      sources([run('b', null, false, 'p'), run('a', null, false, 'q')]),
      null,
    ),
    2,
  );
  // Models: one offered is taken (even a paid one, as the only choice); with more, the last real run of THAT app.
  const models = [held(0, 'm1'), held(1, 'm2')];
  assert.equal(defaultRow('models', scope, [held(0, 'm1')], sources([]), 'a'), 0);
  assert.equal(defaultRow('models', scope, models, sources([]), 'a'), null);
  assert.equal(defaultRow('models', scope, models, sources([run('b', 'm2')]), 'a'), null);
  assert.equal(defaultRow('models', scope, models, sources([run('a', 'm2', true)]), 'a'), null);
  assert.equal(defaultRow('models', scope, models, sources([run('a', 'm2')]), 'a'), 1);
  // The unscoped models list has none.
  assert.equal(defaultRow('models', scope, [held(0, 'm1')], sources([]), null), null);
  // Folders: the first new folder offered.
  assert.equal(
    defaultRow(
      'folders',
      scope,
      [held(0, { kind: 'existing_folder' }), held(1, { kind: 'new_folder' })],
      sources([]),
      null,
    ),
    1,
  );
  assert.equal(defaultRow('approval_levels', scope, [held(0, 'ask')], sources([]), null), null);
});

test("one app's models are read by its row and the list's digest; a changed apps list is refused", async (t) => {
  const s = setup(t, [
    app('a', { models: () => [HERE] }),
    app('b', { models: () => [{ ...HERE, model_ref: 'b/x', display_name: 'B X' }] }),
  ]);
  const scope = { project_id: null, workstream_id: null, execution_id: null };
  const apps = await s.h.controlPlane.commands.choices(scope, 'runtimes');
  assert.ok(apps);
  assert.equal(apps.default, null, 'two real apps, none used before');
  const of = await s.h.controlPlane.commands.appModels(scope, {
    runtime: 1,
    runtimes: apps.digest,
  });
  assert.ok(of !== null && of !== 'choices_changed');
  assert.deepEqual(
    of.rows.map((row) => row.label),
    ['B X'],
  );
  assert.equal(of.default, 0, 'its one model');
  assert.equal(
    await s.h.controlPlane.commands.appModels(scope, { runtime: 1, runtimes: 'f'.repeat(64) }),
    'choices_changed',
  );
  // A model index read under one app is refused under the other: its digest is that app's list.
  const { step } = await s.press(
    { instruction: 'x', name: 'X', runtime_id: 0, model_ref: 0 },
    { runtimes: apps.digest, models: of.digest },
  );
  assert.equal(step.status, 'rejected');
  assert.match(step.summary, /list changed/);
});

test('where the app runs its model is not known, the card says who pays and that the idea may leave, never more', async (t) => {
  const s = setup(t, [app('plain')]);
  const { step } = await s.press({ instruction: 'x', name: 'X' });
  assert.equal(step.status, 'waiting', step.summary);
  assert.equal(step.proposal?.facts?.[1]?.value, 'Not Known');
  assert.deepEqual(step.proposal?.warnings, [
    {
      code: 'cost_unknown',
      line: 'Any cost is on the account App plain uses; the final price is not known',
    },
    { code: 'may_leave', line: 'Your idea, and what the agent reads, may leave this computer' },
  ]);
});

test('a starter is a row the person picks, never a default: it goes into the new folder and is said on the card', async (t) => {
  const base = realpathSync(mkdtempSync(join(tmpdir(), 'halcyonic-start-starters-')));
  t.after(() => rmSync(base, { recursive: true, force: true }));
  const trackedDir = fileURLToPath(new URL('../../../../starters', import.meta.url));
  const installedDir = join(base, 'installed');
  const lock = readFileSync(join(trackedDir, 'iwsdk-vr', 'package-lock.json'));
  mkdirSync(join(installedDir, 'iwsdk-vr', 'node_modules'), { recursive: true });
  writeFileSync(
    join(installedDir, 'iwsdk-vr', 'installed.json'),
    JSON.stringify({ format: 1, lockfile: createHash('sha256').update(lock).digest('hex') }),
  );
  const starters = new HostStarters({ trackedDir, installedDir, dataDir: join(base, 'data') });
  const s = setup(t, [app('real', { models: () => [HERE] })], { starters });
  const scope = { project_id: null, workstream_id: null, execution_id: null };
  const list = await s.h.controlPlane.commands.choices(scope, 'starters');
  assert.ok(list);
  assert.equal(list.default, null, 'no starter unless picked');
  const vr = list.rows.findIndex((row) => row.label === 'A VR app you can try in the headset');
  assert.ok(vr >= 0, JSON.stringify(list.rows));
  // No pick: a plain new folder.
  const plain = await s.press({ instruction: 'Tide pools', name: 'Tides' });
  assert.equal(plain.step.status, 'waiting', plain.step.summary);
  const plainHeld = s.h.controlPlane.projection.workspaceProposal(plain.id, plain.step.step_id);
  assert.equal(
    (plainHeld?.before?.[0]?.payload as { location: { kind: string } } | undefined)?.location.kind,
    'new_folder',
  );
  // A pick: the new folder begins from it, said in what happens and in the folder fact.
  const picked = await s.press(
    { instruction: 'Tide pools', name: 'Tides', starter: vr },
    { starters: list.digest },
  );
  assert.equal(picked.step.status, 'waiting', picked.step.summary);
  const held = s.h.controlPlane.projection.workspaceProposal(picked.id, picked.step.step_id);
  assert.deepEqual((held?.before?.[0]?.payload as { location: unknown } | undefined)?.location, {
    kind: 'new_from_starter',
    root: s.root,
    folder_name: 'Tides',
    starter_id: 'iwsdk-vr',
  });
  assert.equal(
    picked.step.proposal?.what_happens,
    'Makes the project and its new folder from A VR app you can try in the headset, then starts the agent working on your idea',
  );
  assert.equal(
    picked.step.proposal?.facts?.[2]?.value,
    'New Folder Tides From A VR app you can try in the headset',
  );
  // A starter list read before it changed is refused, and nothing is made.
  const stale = await s.press(
    { instruction: 'Tide pools', name: 'Tides', starter: vr },
    { starters: 'f'.repeat(64) },
  );
  assert.equal(stale.step.status, 'rejected');
  assert.match(stale.step.summary, /starter list changed/);
  assert.equal(s.made().projects, 0);
  // An existing project takes no starter.
  const created = s.h.controlPlane.commands.submit(s.h.commands.createProject('Kept'), 'internal');
  const projectId =
    created.command?.result?.kind === 'project_created' ? created.command.result.project_id : '';
  const kept = await s.press(
    { instruction: 'x', starter: vr },
    { starters: list.digest },
    projectId,
  );
  assert.equal(kept.step.status, 'rejected');
  assert.match(kept.step.summary, /starter is not given here/);
});

test('a project taken off the list: a start in it is refused, and a start on its folder says it brings it back (ADR 0063)', async (t) => {
  const s = setup(t, [app('real', { models: () => [HERE], folder: true })]);
  mkdirSync(join(s.root, 'tides'));
  const created = s.h.controlPlane.commands.submit(
    s.h.commands.createProject('Tide Pools', {
      kind: 'existing_folder',
      root: s.root,
      folder_name: 'tides',
    }),
    'internal',
  );
  const projectId =
    created.command?.result?.kind === 'project_created' ? created.command.result.project_id : '';
  const review = removalReview(s.h.controlPlane.projection, projectId);
  assert.ok(review.ok);
  const removed = s.h.controlPlane.commands.submit(
    {
      ...s.h.commands.createProject('unused'),
      command_type: 'project.remove_from_list',
      payload: { project_id: projectId, review: review.review.digest },
    } as never,
    'internal',
  );
  assert.equal(removed.command?.status, 'completed', JSON.stringify(removed.command?.failure));
  // A start in its scope is refused before anything is prepared or made.
  const inIt = s.h.controlPlane.commands.submit(
    {
      ...s.h.commands.createProject('unused'),
      command_type: 'workspace.request.submit',
      payload: {
        project_id: projectId,
        workstream_id: null,
        execution_id: null,
        text: 'Start Building',
        act: { command_type: 'execution.start', values: { instruction: 'x' }, lists: {} },
      },
    } as never,
    'http',
    { kind: 'local' },
  );
  assert.equal(inIt.command?.rejection?.code, 'project_not_found');
  // New Project on its folder: the card says it brings the project back, by its own name, and the name typed is not said.
  const scope = { project_id: null, workstream_id: null, execution_id: null };
  const folders = await s.h.controlPlane.commands.choices(scope, 'folders');
  assert.ok(folders);
  const row = folders.rows.findIndex((each) => each.label === 'tides');
  assert.ok(row >= 0, JSON.stringify(folders.rows.map((each) => each.label)));
  const { id, step } = await s.press(
    { instruction: 'More tides', name: 'Another Name', location: row },
    { folders: folders.digest },
  );
  assert.equal(step.status, 'waiting', step.summary);
  assert.equal(
    step.proposal?.what_happens,
    'Brings back Tide Pools, then starts the agent working on your idea',
  );
  assert.ok(!step.proposal?.said.some((each) => each.words === 'Another Name'));
  s.confirm(id, step);
  await settle();
  const after = s.step(id, step.step_id);
  assert.equal(after.made?.project_id, projectId, 'the same project, brought back');
  assert.equal(s.h.controlPlane.projection.projects().length, 1);
});

test("the level says what the run will really ask: a confined app's own work goes ahead without a card (the no-ask unit)", async (t) => {
  let firstWarning: unknown;
  const level = async (options: {
    kind?: string;
    outside?: boolean;
    starter?: boolean;
    levelAt?: () => 'ask' | 'never_ask' | null;
  }) => {
    let folder = '';
    const s = setup(
      t,
      [
        app('real', {
          models: () => [HERE],
          ...(options.kind === undefined ? {} : { kind: options.kind }),
          ...(options.outside === undefined ? {} : { outside: options.outside }),
          ...(options.levelAt === undefined ? {} : { levelAt: options.levelAt }),
        }),
      ],
      {
        confinedReads: (directory) =>
          options.starter === false || directory !== folder ? null : { readable: [] },
      },
    );
    folder = join(s.root, 'kept');
    mkdirSync(folder);
    const created = s.h.controlPlane.commands.submit(
      s.h.commands.createProject('Kept', {
        kind: 'existing_folder',
        root: s.root,
        folder_name: 'kept',
      }),
      'internal',
    );
    const projectId =
      created.command?.result?.kind === 'project_created' ? created.command.result.project_id : '';
    const { step } = await s.press({ instruction: 'Add a map' }, {}, projectId);
    assert.equal(step.status, 'waiting', step.summary);
    firstWarning = step.proposal?.warnings?.[0];
    return step.proposal?.facts?.[3]?.value;
  };
  // A confined app the host answers for, in a starter's project: it asks only for what reaches outside its folder.
  assert.equal(await level({ kind: 'opencode' }), 'Asks Only Outside Task Folder');
  // Outside the sandbox, or a folder of the person's own: it asks first, as before.
  assert.equal(await level({ kind: 'opencode', outside: true }), 'Asks First');
  assert.equal(await level({ kind: 'opencode', starter: false }), 'Asks First');
  // An app whose adapter says the start will run without asking (Codex on a starter's project, ADR 0041; its real
  // choice is tested in runtimes.test.ts) is carded so, whatever was asked for: never "Asks First" over a run that
  // asks nothing (AUDIT2, 21:26Z).
  assert.equal(await level({ kind: 'codex', levelAt: () => 'never_ask' }), 'Works Without Asking');
  // ... and says it first among the warnings, in words of its own.
  assert.deepEqual(firstWarning, {
    code: 'never_asks',
    line: 'Runs commands and changes files without asking you',
  });
  assert.equal(await level({ kind: 'codex', levelAt: () => 'ask' }), 'Asks First');
  // An adapter that cannot say for these bounds: not known, never a guess, and no never-asks line either.
  assert.equal(await level({ kind: 'codex', levelAt: () => null }), 'Not Known');
  assert.notEqual((firstWarning as { code?: string } | undefined)?.code, 'never_asks');
});
