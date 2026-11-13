import assert from 'node:assert/strict';
import { randomUUID } from 'node:crypto';
import { mkdirSync, mkdtempSync, realpathSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { after, test } from 'node:test';
import type { CommandId, ProjectId } from '@halcyonic/contracts';
import { createHostLocations } from '../locations.ts';
import { createTestControlPlane, TEST_CLIENT } from '../testing/harness.ts';
import { readRules, rulesReview } from './project-rules.ts';
import { locationReview } from './review.ts';
import { buildReview } from './reviews.ts';

const root = realpathSync(mkdtempSync(join(tmpdir(), 'halcyonic-reviews-')));
after(() => rmSync(root, { recursive: true, force: true }));

test('the one review: what the list offers, prepared from values, reviewed with the button’s own digest (ADR 0060)', async () => {
  mkdirSync(join(root, 'app'));
  mkdirSync(join(root, 'other'));
  writeFileSync(join(root, 'app', 'AGENTS.md'), 'Use tabs.\n');
  const { controlPlane, commands } = createTestControlPlane({
    locations: createHostLocations([root]),
  });
  const created = controlPlane.commands.submit(
    commands.createProject('Reviewed', { kind: 'existing_folder', root, folder_name: 'app' }),
    'internal',
  );
  const projectId = (
    created.command?.result?.kind === 'project_created' ? created.command.result.project_id : ''
  ) as ProjectId;
  const scope = { project_id: projectId, workstream_id: null, execution_id: null };
  const given = (command_type: string, values: Record<string, unknown>) => ({
    scope,
    command_type,
    values: values as Record<string, boolean>,
    read: {},
    command_id: randomUUID() as CommandId,
    issued_at: '2026-11-12T05:40:00.000Z',
    client: TEST_CLIENT,
  });

  // Project Rules: offered with a readable rules file, its review the button's own digest.
  const rules = (await controlPlane.commands.here(scope)).find(
    (action) => action.kind === 'command' && action.command_type === 'project.rules.set',
  );
  assert.ok(rules?.kind === 'command' && rules.confirm === 'review');
  const prepared = await controlPlane.commands.prepare(given('project.rules.set', { on: true }));
  assert.ok(prepared.ok, prepared.ok ? '' : prepared.message);
  const built = await buildReview(controlPlane, prepared.command);
  assert.ok(built.ok, built.ok ? '' : built.message);
  const read = readRules(join(root, 'app'));
  assert.ok(read.state === 'ok');
  assert.equal(built.review.digest, rulesReview(projectId, read.file, read.sha256));
  assert.equal(built.review.confirm_words, 'Use Rules');
  assert.deepEqual(built.review.said, [], 'a yes or no is not words that go out');
  assert.deepEqual(built.review.facts, [{ kind: 'words', value: 'AGENTS.md', theirs: false }]);
  assert.equal(
    built.review.items[0]?.theirs,
    true,
    'the file’s words are the project’s, said as theirs',
  );

  // Carried back, the digest is the one the command's own admission checks: it is admitted.
  const sent = controlPlane.commands.submit(
    {
      ...prepared.command,
      payload: { ...prepared.command.payload, review: built.review.digest },
    } as typeof prepared.command,
    'internal',
  );
  assert.equal(sent.disposition, 'accepted');

  // A move's review names both folders and digests as the location review does.
  const move = {
    ...commands.setLocation(projectId, { kind: 'existing_folder', root, folder_name: 'other' }),
  };
  const moved = await buildReview(controlPlane, move);
  assert.ok(moved.ok);
  const project = controlPlane.projection.project(projectId);
  assert.equal(
    moved.review.digest,
    locationReview(projectId, move.payload.location, project?.location ?? null),
  );
  assert.equal(moved.review.facts.length, 2);

  // A command confirmed by its one press has no review.
  const none = await buildReview(
    controlPlane,
    commands.createWorkstream(projectId, {
      title: 'Night',
      objective: null,
    } as never),
  );
  assert.ok(!none.ok);
});

test('a review says whole the words given that go out with the command, by their labels (SCREENS, addition D)', async () => {
  const { controlPlane, commands } = createTestControlPlane({});
  const command = {
    ...commands.createProject('Words', null),
    command_type: 'project.send',
    payload: {
      project_id: '01920000-0000-7000-8000-00000000e001',
      title: 'Make the sky night',
      review: '0'.repeat(64),
    },
  } as never;
  // Send has no folder here, so its review is refused, yet what it would say is computed the one way.
  const built = await buildReview(controlPlane, command);
  assert.ok(!built.ok);
  const { saidIn } = await import('./reviews.ts');
  assert.deepEqual(saidIn(command), [{ label: 'Title', words: 'Make the sky night' }]);
});

test('Remove Secret is reviewed, since its value cannot be brought back; Take Down’s commit is the host’s own (AUDIT2 S2, the lead 05:49Z)', async () => {
  const { controlPlaneDraft: controlDraft, NO_CAUSE } = await import('./drafts.ts');
  const { controlPlane, commands } = createTestControlPlane({});
  const created = controlPlane.commands.submit(commands.createProject('Kept', null), 'internal');
  const projectId = (
    created.command?.result?.kind === 'project_created' ? created.command.result.project_id : ''
  ) as ProjectId;
  const scope = { project_id: projectId, workstream_id: null, execution_id: null };
  const at = '2026-11-12T05:55:00.000Z' as never;
  controlPlane.recorder.record(
    controlDraft(
      'project.secret.stored',
      scope,
      { name: 'API_KEY', replaced: false },
      at,
      NO_CAUSE,
    ),
  );
  controlPlane.recorder.record(
    controlDraft(
      'project.published',
      scope,
      {
        repository: 'someone/kept',
        check_id: '01920000-0000-7000-8000-00000000f001',
        branch: 'halcyonic-pages',
        commit: 'c'.repeat(40),
        address: null,
      } as never,
      at,
      NO_CAUSE,
    ),
  );
  const remove = {
    ...commands.createProject('x', null),
    command_type: 'project.secret.remove',
    payload: { project_id: projectId, name: 'API_KEY' },
  } as never;
  const built = await buildReview(controlPlane, remove);
  assert.ok(built.ok, built.ok ? '' : built.message);
  assert.equal(built.review.confirm_words, 'Remove Secret');
  assert.deepEqual(
    built.review.warnings.map((w) => w.code),
    ['cannot_undo'],
  );
  // Take Down by its one press: the commit is the one the host recorded, never a proposer's.
  const { prepareCommand } = await import('./prepare.ts');
  const down = await prepareCommand(
    {
      scope,
      command_type: 'project.unpublish',
      values: {},
      read: {},
      command_id: randomUUID() as CommandId,
      issued_at: '2026-11-12T05:55:00.000Z',
      client: TEST_CLIENT,
    },
    {
      publishedCommit: (id) => controlPlane.projection.project(id)?.published?.commit ?? null,
      rows: async () => null,
    },
  );
  assert.ok(down.ok, down.ok ? '' : down.message);
  assert.equal((down.command.payload as { commit?: string }).commit, 'c'.repeat(40));
  const smuggled = await prepareCommand(
    {
      scope,
      command_type: 'project.unpublish',
      values: { commit: 'd'.repeat(40) },
      read: {},
      command_id: randomUUID() as CommandId,
      issued_at: '2026-11-12T05:55:00.000Z',
      client: TEST_CLIENT,
    },
    { publishedCommit: () => 'c'.repeat(40), rows: async () => null },
  );
  assert.ok(!smuggled.ok, 'the commit is never a proposer’s to give');
});

test("a first Send's one review names the repository it makes and who can see it, with the host's own title (ADR 0039, amended)", async () => {
  const { standInGitHub } = await import('../testing/github-stand-in.ts');
  const base = realpathSync(mkdtempSync(join(tmpdir(), 'halcyonic-first-send-')));
  after(() => rmSync(base, { recursive: true, force: true }));
  const stand = standInGitHub(base);
  mkdirSync(join(stand.projects, 'plain'));
  writeFileSync(join(stand.projects, 'plain', 'index.html'), '<title>Plain</title>\n');
  const { controlPlane, commands } = createTestControlPlane({
    locations: createHostLocations([stand.projects]),
    github: {
      git: '/usr/bin/git',
      gh: stand.ghPath,
      dataDir: join(base, 'data'),
      env: { HOME: stand.home },
    },
  });
  const created = controlPlane.commands.submit(
    commands.createProject('Plain Game', {
      kind: 'existing_folder',
      root: stand.projects,
      folder_name: 'plain',
    }),
    'internal',
  );
  const projectId = (
    created.command?.result?.kind === 'project_created' ? created.command.result.project_id : ''
  ) as ProjectId;
  const send = (payload: Record<string, unknown>) =>
    ({
      ...commands.createProject('unused', null),
      command_type: 'project.send',
      payload: { project_id: projectId, review: '0'.repeat(64), ...payload },
    }) as never;
  const built = await buildReview(controlPlane, send({}));
  assert.ok(built.ok, built.ok ? '' : built.message);
  assert.equal(built.review.confirm_words, 'Send 1 File');
  assert.match(built.review.what_happens, /A new private repository of yours holds the project/);
  assert.deepEqual(built.review.facts, [
    { kind: 'repository', value: 'plain', theirs: true },
    { kind: 'branch', value: 'main', theirs: false },
    { kind: 'words', value: 'Plain Game', theirs: true },
  ]);
  assert.deepEqual(built.review.warnings, []);
  const shown = await buildReview(
    controlPlane,
    send({ visibility: 'public', repository_name: 'plain-game' }),
  );
  assert.ok(shown.ok);
  assert.equal(shown.review.facts[0]?.value, 'plain-game');
  assert.deepEqual(shown.review.warnings, [
    { code: 'public', line: 'Anyone can see its code and history' },
  ]);
  assert.notEqual(
    shown.review.digest,
    built.review.digest,
    'what the press sends is in the digest',
  );
});

test('a version row offers only what it can do: one holding a task’s work never added starts no task (LOOP’s run)', async () => {
  const { controlPlaneDraft: draft, NO_CAUSE } = await import('./drafts.ts');
  mkdirSync(join(root, 'versions-app'), { recursive: true });
  const { controlPlane, commands } = createTestControlPlane({
    locations: createHostLocations([root]),
  });
  const made = controlPlane.commands.submit(
    commands.createProject('Versions', {
      kind: 'existing_folder',
      root,
      folder_name: 'versions-app',
    }),
    'internal',
  );
  const projectId = (
    made.command?.result?.kind === 'project_created' ? made.command.result.project_id : ''
  ) as ProjectId;
  const task = controlPlane.commands.submit(
    commands.createWorkstream(projectId, { title: 'Copy', objective: null } as never),
    'internal',
  );
  const workstreamId =
    task.command?.result?.kind === 'workstream_created' ? task.command.result.workstream_id : '';
  const at = '2026-11-12T06:50:00.000Z' as never;
  const taskScope = {
    project_id: projectId,
    workstream_id: workstreamId,
    execution_id: null,
  } as never;
  const copy = join(root, 'copy-of-app');
  controlPlane.recorder.record(
    draft(
      'workstream.copy_made',
      taskScope,
      { directory: copy, base_tree: 'a'.repeat(64), files: 1 },
      at,
      NO_CAUSE,
    ),
  );
  const executionId = '01920000-0000-7000-8000-00000000c0de';
  controlPlane.recorder.record(
    draft(
      'execution.created',
      { project_id: projectId, workstream_id: workstreamId, execution_id: executionId } as never,
      {
        runtime: { runtime_id: 'mock', kind: 'mock', display_name: 'Mock', synthetic: true },
        instruction: 'Work.',
        directory: copy,
      } as never,
      at,
      NO_CAUSE,
    ),
  );
  controlPlane.recorder.record(
    draft(
      'project.checkpoint.recorded',
      { project_id: projectId, workstream_id: null, execution_id: null },
      {
        checkpoint_id: '01920000-0000-7000-8000-00000000c0df',
        reason: 'after_turn',
        execution_id: executionId,
        tree: 'b'.repeat(64),
        files: 1,
        project_head: null,
      } as never,
      at,
      NO_CAUSE,
    ),
  );
  const scope = { project_id: projectId, workstream_id: null, execution_id: null };
  const versions = await controlPlane.commands.choices(scope, 'versions');
  assert.ok(versions !== null && versions.rows.length === 1);
  assert.ok(
    !versions.rows[0]?.actions.includes('workstream.create'),
    'no task starts from a version holding work never added',
  );
  const prepared = await controlPlane.commands.prepare({
    scope,
    command_type: 'workstream.create',
    values: { title: 'From then', from_checkpoint: 0 } as never,
    read: { versions: versions.digest },
    command_id: randomUUID() as CommandId,
    issued_at: '2026-11-12T06:50:00.000Z',
    client: TEST_CLIENT,
  });
  assert.ok(!prepared.ok, 'and the preparation refuses that row for it');
});

test("Add to Project's review names a change to a provider's declaration, above all where its key goes (AUDIT2 N1)", async (t) => {
  const { mkdirSync, mkdtempSync, rmSync, writeFileSync } = await import('node:fs');
  const { tmpdir } = await import('node:os');
  const { join } = await import('node:path');
  const { providerWarnings } = await import('./reviews.ts');
  const base = mkdtempSync(join(tmpdir(), 'halcyonic-provider-review-'));
  t.after(() => rmSync(base, { recursive: true, force: true }));
  const declaration = (id: string, name: string, upstream: string, file = `${id}-key`) =>
    JSON.stringify({
      format: 1,
      id,
      name,
      upstream,
      speaks: ['chat_completions'],
      key: {
        file,
        variable: 'K',
        field: 'Key',
        header: { name: 'Authorization', scheme: 'Bearer' },
      },
      models: ['m'],
    });
  const put = (root: string, path: string, body: string) => {
    mkdirSync(join(root, path, '..'), { recursive: true });
    writeFileSync(join(root, path), body);
  };
  const project = join(base, 'project');
  const copy = join(base, 'copy');
  put(
    project,
    'providers/alpha/provider.json',
    declaration('alpha', 'Alpha', 'https://a.example/v1'),
  );
  put(
    copy,
    'providers/alpha/provider.json',
    declaration('alpha', 'Alpha', 'https://elsewhere.example/v1'),
  );
  put(
    project,
    'providers/gamma/provider.json',
    declaration('gamma', 'Gamma', 'https://g.example/v1'),
  );
  put(
    project,
    'providers/delta/provider.json',
    declaration('delta', 'Delta', 'https://d.example/v1'),
  );
  put(
    copy,
    'providers/delta/provider.json',
    declaration('delta', 'Delta', 'https://d.example/v1', 'other'),
  );
  put(copy, 'providers/beta/provider.json', declaration('beta', 'Beta', 'https://b.example/v1'));
  put(copy, 'providers/notes/provider.json', '{"not": "a declaration"}');
  put(copy, 'src/app.ts', 'export {};');
  const warnings = providerWarnings(
    {
      files: [
        { path: 'providers/alpha/provider.json', change: 'changed' },
        { path: 'providers/beta/provider.json', change: 'added' },
        { path: 'providers/gamma/provider.json', change: 'removed' },
        { path: 'providers/delta/provider.json', change: 'changed' },
        { path: 'providers/notes/provider.json', change: 'added' },
        { path: 'src/app.ts', change: 'added' },
      ],
      total: 6,
    } as never,
    copy,
    project,
  );
  assert.deepEqual(
    warnings.map((warning) => warning.line),
    [
      'Sends the Alpha key to elsewhere.example, not a.example (providers/alpha/provider.json)',
      'Adds where a key goes: Beta at b.example (providers/beta/provider.json)',
      'Removes the Gamma provider (providers/gamma/provider.json)',
      'Changes how the Delta key is kept or sent (providers/delta/provider.json)',
    ],
    'a file that is no declaration the host would read, and any other file, says nothing',
  );
  assert.ok(warnings.every((warning) => warning.code === 'provider_declaration'));
  const truncated = providerWarnings(
    { files: [{ path: 'src/app.ts', change: 'added' }], total: 900 } as never,
    copy,
    project,
  );
  assert.deepEqual(
    truncated.map((warning) => warning.code),
    ['provider_declaration_unread'],
    'unknown stays unknown: more files changed than were listed',
  );
});

test("Send One Change's one review says what goes, that none of it holds the secret, and what stays behind (AUDIT2's words, 16:42Z)", async () => {
  const { standInGitHub } = await import('../testing/github-stand-in.ts');
  const base = realpathSync(mkdtempSync(join(tmpdir(), 'halcyonic-one-change-')));
  after(() => rmSync(base, { recursive: true, force: true }));
  const stand = standInGitHub(base);
  const value = 'svc-held-value-0123456789abcdef';
  // Two files like .env that only the unsent versions added: both kept back, counted on the card (AUDIT2 S5).
  writeFileSync(join(stand.folder, '.env'), 'MODE=dev\n');
  writeFileSync(join(stand.folder, '.env.local'), 'MODE=local\n');
  writeFileSync(join(stand.folder, 'key.txt'), `KEY=${value}\n`);
  stand.git(stand.folder, 'add', '-A');
  stand.git(stand.folder, 'commit', '-q', '-m', 'with key');
  stand.git(stand.folder, 'rm', '-q', 'key.txt');
  stand.git(stand.folder, 'commit', '-q', '-m', 'without key');
  writeFileSync(join(stand.folder, 'app.txt'), 'lanterns\n');
  const { controlPlane, commands } = createTestControlPlane({
    locations: createHostLocations([stand.projects]),
    github: {
      git: '/usr/bin/git',
      gh: stand.ghPath,
      dataDir: join(base, 'data'),
      env: { HOME: stand.home },
      held: () => [{ what: 'MY_SERVICE_KEY', value }],
    },
  });
  const created = controlPlane.commands.submit(
    commands.createProject('Garden', {
      kind: 'existing_folder',
      root: stand.projects,
      folder_name: 'garden',
    }),
    'internal',
  );
  const projectId = (
    created.command?.result?.kind === 'project_created' ? created.command.result.project_id : ''
  ) as ProjectId;
  const github = controlPlane.github;
  assert.ok(github !== null);
  const plain = await github.review(projectId, stand.folder, [], 'Lanterns');
  assert.ok(plain.ok, plain.ok ? '' : plain.message);
  const refused = await github.send(projectId, stand.folder, [], 'Lanterns', plain.send.review);
  assert.equal(refused.ok ? null : refused.code, 'holds_secret_in_history');
  const built = await buildReview(controlPlane, {
    ...commands.createProject('unused', null),
    command_type: 'project.send',
    payload: { project_id: projectId, title: 'Lanterns', one_change: true, review: '0'.repeat(64) },
  } as never);
  assert.ok(built.ok, built.ok ? '' : built.message);
  assert.equal(built.review.confirm_words, 'Send One Change');
  assert.equal(
    built.review.what_happens,
    "Sends the project's files as they are now to owner/garden, as one change. None of the files sent holds MY_SERVICE_KEY.",
  );
  assert.deepEqual(
    built.review.facts.map((fact) =>
      fact.kind === 'branch' && !fact.theirs ? { ...fact, value: fact.value.slice(0, 16) } : fact,
    ),
    [
      { kind: 'repository', value: 'github.com/owner/garden', theirs: true },
      { kind: 'branch', value: 'halcyonic/garden', theirs: false },
      { kind: 'branch', value: 'main', theirs: true },
      { kind: 'words', value: 'Lanterns', theirs: true },
    ],
  );
  assert.deepEqual(built.review.warnings, [
    {
      code: 'history_left',
      line: 'Your earlier versions still hold MY_SERVICE_KEY. Do not send them another way.',
    },
    {
      code: 'one_change',
      line: 'The repository gets one change, not your earlier versions one by one.',
    },
    {
      code: 'stays_sent',
      line: 'Once sent, anyone who can read owner/garden can read it.',
    },
    {
      code: 'files_kept_back',
      line: '.env and 1 more are not sent, since files like them can hold secrets.',
    },
  ]);
  assert.ok(!JSON.stringify(built.review).includes(value), 'never the value');
});
