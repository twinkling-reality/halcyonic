import assert from 'node:assert/strict';
import {
  mkdirSync,
  mkdtempSync,
  readdirSync,
  realpathSync,
  rmSync,
  statSync,
  writeFileSync,
} from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { after, describe, test } from 'node:test';
import type {
  CommandEnvelope,
  EventEnvelope,
  EventsResponse,
  LocationsResponse,
  ProjectId,
  ProjectsResponse,
  RemovalReviewResponse,
  Snapshot,
  WorkstreamId,
  WorkstreamsResponse,
} from '@halcyonic/contracts';
import { OFF_LIST_WORDS, Projection } from '@halcyonic/domain';
import { DEMO_WORKSTREAMS } from '../demo-plan.ts';
import { shownEvent } from '../http/realtime.ts';
import { createHostLocations } from '../locations.ts';
import { pageSecrets } from '../network/setup-page.ts';
import { createTestControlPlane, startTestServer } from '../testing/harness.ts';
import { removalReview } from './removal.ts';

const FEATURE = DEMO_WORKSTREAMS[0] as (typeof DEMO_WORKSTREAMS)[number];
const base = realpathSync(mkdtempSync(join(tmpdir(), 'halcyonic-remove-from-list-')));
after(() => rmSync(base, { recursive: true, force: true }));
const VALUE = 'test-value-not-real-0063';

/** A project root with a folder `game` holding two files, and a control plane that may bind it. */
function setUp(name: string, options: { path?: string; dataDir?: string } = {}) {
  const root = join(base, name);
  mkdirSync(join(root, 'game', 'src'), { recursive: true });
  writeFileSync(join(root, 'game', 'README.md'), 'A tide game.\n');
  writeFileSync(join(root, 'game', 'src', 'main.js'), 'console.log(1);\n');
  return { root, ...harnessAt(root, options) };
}

function harnessAt(root: string, options: { path?: string; dataDir?: string } = {}) {
  const harness = createTestControlPlane({
    locations: createHostLocations([root]),
    ...(options.path === undefined ? {} : { path: options.path }),
    ...(options.dataDir === undefined ? {} : { projectSecrets: { dataDir: options.dataDir } }),
  });
  const { controlPlane, commands } = harness;
  const submit = (command: CommandEnvelope) => controlPlane.commands.submit(command, 'internal');
  const add = (name = 'Tide Game', folder = 'game') => {
    const outcome = submit(
      commands.createProject(name, { kind: 'existing_folder', root, folder_name: folder }),
    );
    const result = outcome.command?.result;
    if (result?.kind !== 'project_created') throw new Error('no project');
    return result;
  };
  const task = (projectId: ProjectId): WorkstreamId => {
    const created = submit(commands.createWorkstream(projectId, FEATURE));
    if (created.command?.result?.kind !== 'workstream_created') throw new Error('no task');
    return created.command.result.workstream_id;
  };
  const removal = (projectId: ProjectId, review?: string): CommandEnvelope => ({
    ...commands.createProject('unused'),
    command_type: 'project.remove_from_list',
    payload: { project_id: projectId, ...(review === undefined ? {} : { review }) },
  });
  const card = (projectId: ProjectId) => {
    const built = removalReview(controlPlane.projection, projectId);
    if (!built.ok) throw new Error(built.message);
    return built.review;
  };
  const remove = (projectId: ProjectId) => submit(removal(projectId, card(projectId).digest));
  return { harness, controlPlane, commands, submit, add, task, removal, card, remove };
}

/** Every file under `folder` with its size and modification time, to show nothing was touched. */
function snapshotOf(folder: string): string[] {
  const out: string[] = [];
  const walk = (at: string) => {
    for (const entry of readdirSync(at, { withFileTypes: true })) {
      const path = join(at, entry.name);
      const stat = statSync(path);
      out.push(`${path} ${stat.size} ${stat.mtimeMs}`);
      if (entry.isDirectory()) walk(path);
    }
  };
  walk(folder);
  return out.sort();
}

describe('taking a project off the list, on the host (ADR 0063)', () => {
  test('needs the card read first, refuses a stale one, and leaves the folder exactly as it was', () => {
    const { root, controlPlane, submit, add, task, removal, card, remove } = setUp('card');
    const { project_id: projectId } = add();
    const before = snapshotOf(join(root, 'game'));
    const shown = card(projectId);
    assert.match(shown.what_happens, /^Tide Game leaves the list\./);
    assert.deepEqual(shown.facts, [{ kind: 'folder', value: join(root, 'game'), theirs: true }]);
    assert.equal(shown.confirm_words, 'Remove From List');

    const bare = submit(removal(projectId));
    assert.equal(bare.disposition, 'rejected');
    // A task made after the card was read changes what the card said.
    task(projectId);
    const stale = submit(removal(projectId, shown.digest));
    assert.equal(stale.disposition, 'rejected');
    assert.equal(controlPlane.projection.listed(projectId), true);

    const done = remove(projectId);
    assert.equal(done.disposition, 'accepted');
    assert.equal(done.command?.status, 'completed');
    assert.equal(controlPlane.projection.listed(projectId), false);
    assert.deepEqual(snapshotOf(join(root, 'game')), before);
  });

  test('a project with no folder is said to leave for good in the card itself, not only its warning (AUDIT2 S6)', () => {
    const { controlPlane, commands, submit, card, remove } = setUp('nofolder');
    const made = submit(commands.createProject('Loose Notes'));
    const projectId =
      made.command?.result?.kind === 'project_created' ? made.command.result.project_id : null;
    assert.ok(projectId !== null);
    const shown = card(projectId);
    assert.equal(
      shown.what_happens,
      'Loose Notes leaves the list for good. It has no folder, so it cannot come back',
    );
    assert.deepEqual(shown.facts, []);
    assert.deepEqual(
      shown.warnings.map((warning) => warning.code),
      ['cannot_undo'],
    );
    assert.equal(remove(projectId).command?.status, 'completed');
    assert.equal(controlPlane.projection.listed(projectId), false);
  });

  test('a project whose folder is gone can be taken off the list', () => {
    const { root, controlPlane, add, remove } = setUp('gone');
    const { project_id: projectId } = add();
    rmSync(join(root, 'game'), { recursive: true, force: true });
    assert.equal(remove(projectId).command?.status, 'completed');
    assert.equal(controlPlane.projection.listed(projectId), false);
  });

  test('adding its folder again brings back the same project with its task; a folder under another name makes a new one', () => {
    const { root, controlPlane, add, task, remove } = setUp('back');
    const { project_id: projectId } = add();
    const workstreamId = task(projectId);
    remove(projectId);
    const again = add('A New Name');
    assert.deepEqual(again, { kind: 'project_created', project_id: projectId, returned: true });
    assert.equal(controlPlane.projection.project(projectId)?.name, 'Tide Game');
    assert.equal(controlPlane.projection.listed(projectId), true);
    assert.deepEqual(
      controlPlane.projection.listedWorkstreams(projectId).map((each) => each.workstream_id),
      [workstreamId],
    );

    remove(projectId);
    mkdirSync(join(root, 'moved'));
    const other = add('Moved', 'moved');
    assert.notEqual(other.project_id, projectId);
    assert.equal(other.returned, undefined);
    assert.equal(controlPlane.projection.listed(projectId), false);
  });

  test('its secret values stay on this computer across a restart, still held, and come back with it', async () => {
    const dataDir = join(base, 'secrets-data');
    const path = join(base, 'secrets-journal.sqlite');
    const first = setUp('secrets', { dataDir, path });
    const { project_id: projectId } = first.add();
    const store = first.controlPlane.projectSecrets;
    assert.ok(store !== null);
    let ids = 0;
    const page = pageSecrets({
      submit: (command, via, principal) =>
        first.controlPlane.commands.submit(command, via, principal),
      store,
      projects: () =>
        first.controlPlane.projection.listedProjects().map((each) => ({
          project_id: each.project_id,
          name: each.name,
          secrets: each.secrets ?? [],
        })),
      without: () => [],
      outside: () => [],
      nextId: () => {
        ids += 1;
        return `01a19000-0000-7000-8000-${String(ids).padStart(12, '0')}`;
      },
      now: () => first.harness.time.now(),
    });
    assert.equal(page.set(projectId, 'PROJECT_TEST_KEY', VALUE), null);
    assert.equal(first.remove(projectId).command?.status, 'completed');
    await first.controlPlane.close();

    const second = harnessAt(first.root, { dataDir, path });
    const kept = second.controlPlane.projectSecrets;
    assert.ok(kept !== null);
    assert.equal(second.controlPlane.projection.listed(projectId), false);
    assert.deepEqual(kept.environment(projectId, ['PROJECT_TEST_KEY']), {
      PROJECT_TEST_KEY: VALUE,
    });
    assert.deepEqual(
      kept.held().map((each) => each.what),
      ['PROJECT_TEST_KEY'],
    );
    assert.equal(second.add().project_id, projectId);
    assert.deepEqual(second.controlPlane.projection.project(projectId)?.secrets, [
      'PROJECT_TEST_KEY',
    ]);
    await second.controlPlane.close();
  });
});

describe('what a person or a client reads of a project taken off the list (ADR 0063)', () => {
  test('the snapshot, the lists, the routes by id and the events by task leave it out; its folder reads as free', async () => {
    const root = join(base, 'routes');
    mkdirSync(join(root, 'game'), { recursive: true });
    const server = await startTestServer({ locations: createHostLocations([root]) });
    try {
      const { controlPlane, commands } = server;
      const submit = (command: CommandEnvelope) =>
        controlPlane.commands.submit(command, 'internal');
      const created = submit(
        commands.createProject('Tide Game', { kind: 'existing_folder', root, folder_name: 'game' }),
      );
      const projectId =
        created.command?.result?.kind === 'project_created'
          ? created.command.result.project_id
          : null;
      assert.ok(projectId !== null);
      const start = controlPlane.journal.head();
      const made = submit(commands.createWorkstream(projectId, FEATURE));
      for (const title of ['Second', 'Third', 'Fourth'])
        submit(commands.createWorkstream(projectId, { ...FEATURE, title }));
      const workstreamId =
        made.command?.result?.kind === 'workstream_created'
          ? made.command.result.workstream_id
          : null;
      assert.ok(workstreamId !== null);
      const get = async (path: string) => {
        const response = await fetch(`${server.baseUrl}${path}`, {
          headers: { authorization: `Bearer ${server.token}` },
        });
        return { status: response.status, body: (await response.json()) as unknown };
      };
      const shown = (await get(`/api/projects/${projectId}/remove-review`))
        .body as RemovalReviewResponse;
      assert.equal(shown.availability, 'available');
      if (shown.availability !== 'available') return;
      assert.equal(shown.review.command_type, 'project.remove_from_list');
      const removed = submit({
        ...commands.createProject('unused'),
        command_type: 'project.remove_from_list',
        payload: { project_id: projectId, review: shown.review.digest },
      });
      assert.equal(removed.command?.status, 'completed');
      // A page counts the events it gives: the tasks' events, withheld now, do not leave it short (AUDIT2 S1).
      const page = (await get(`/api/events?after=${start}&limit=2`)).body as EventsResponse;
      assert.equal(
        page.events.length,
        2,
        JSON.stringify(page.events.map((s) => s.event.event_type)),
      );
      assert.ok(
        page.events.every(
          (stored) =>
            stored.event.event_type === 'project.removed_from_list' ||
            stored.event.event_type.startsWith('command.'),
        ),
      );

      const snapshot = (await get('/api/snapshot')).body as Snapshot;
      assert.deepEqual(snapshot.projects, []);
      assert.deepEqual(snapshot.workstreams, []);
      assert.ok(
        snapshot.commands.every(
          (each) => each.project_id === null || each.command_type === 'project.remove_from_list',
        ),
      );
      assert.deepEqual(((await get('/api/projects')).body as ProjectsResponse).projects, []);
      assert.deepEqual(
        ((await get('/api/workstreams')).body as WorkstreamsResponse).workstreams,
        [],
      );
      const locations = (await get('/api/locations')).body as LocationsResponse;
      assert.deepEqual(
        locations.roots[0]?.folders.map((folder) => [folder.name, folder.used_by]),
        [['game', []]],
      );
      // A list read by scope holds nothing of it, and its folder is not read for one (critic, c634b2aba).
      for (const list of ['tasks', 'versions', 'pictures'])
        assert.equal(
          (await get(`/api/actions/choices?project_id=${projectId}&list=${list}`)).status,
          404,
          list,
        );
      assert.deepEqual(
        ((await get(`/api/actions?project_id=${projectId}`)).body as { actions: unknown[] })
          .actions,
        [],
      );
      for (const path of [
        `/api/projects/${projectId}/history`,
        `/api/projects/${projectId}/remove-review`,
        `/api/events?workstream_id=${workstreamId}`,
      ]) {
        const answer = await get(path);
        assert.equal(answer.status, 404, path);
        assert.deepEqual(
          answer.body,
          { error: { code: 'project_not_found', message: OFF_LIST_WORDS, issues: [] } },
          path,
        );
      }
    } finally {
      await server.stop();
    }
  });

  test('a client receives the removal and its command, and nothing else of the project while it is off', () => {
    const projection = new Projection();
    const harness = createTestControlPlane();
    const created = harness.controlPlane.commands.submit(
      harness.commands.createProject('Off'),
      'internal',
    );
    const projectId =
      created.command?.result?.kind === 'project_created'
        ? created.command.result.project_id
        : null;
    assert.ok(projectId !== null);
    const events: EventEnvelope[] = [...harness.journal.readAll()].map((stored) => stored.event);
    for (const stored of harness.journal.readAll()) projection.apply(stored);
    const removal = harness.controlPlane.commands.submit(
      {
        ...harness.commands.createProject('unused'),
        command_type: 'project.remove_from_list',
        payload: {
          project_id: projectId,
          review: (() => {
            const built = removalReview(harness.controlPlane.projection, projectId);
            if (!built.ok) throw new Error(built.message);
            return built.review.digest;
          })(),
        },
      },
      'internal',
    );
    assert.equal(removal.command?.status, 'completed');
    const after = [...harness.journal.readAll()].slice(events.length);
    for (const stored of after) projection.apply(stored);
    const sent = after
      .filter((stored) => shownEvent(projection, stored.event))
      .map((stored) => stored.event.event_type);
    assert.deepEqual(sent, ['command.accepted', 'project.removed_from_list', 'command.completed']);
    // Anything later of the project reaches no client.
    const later = {
      ...(after.at(-1)?.event as EventEnvelope),
      event_type: 'project.secret.removed',
      payload: { name: 'X_KEY' },
    } as unknown as EventEnvelope;
    assert.equal(shownEvent(projection, later), false);
  });
});
