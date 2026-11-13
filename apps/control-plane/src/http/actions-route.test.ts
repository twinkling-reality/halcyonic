import assert from 'node:assert/strict';
import { test } from 'node:test';
import type { ChoicesResponse, HereAction, HereResponse } from '@halcyonic/contracts';
import { startTestServer } from '../testing/harness.ts';

const types = (actions: readonly HereAction[]) =>
  actions.map((action) =>
    action.kind === 'command' ? action.command_type : `list:${action.list}`,
  );

test('GET /api/actions lists what can be done where the person stands, from the host’s own admission (ADR 0060)', async (t) => {
  const server = await startTestServer();
  t.after(() => server.stop());
  const get = async (query: string) => {
    const response = await fetch(`${server.baseUrl}/api/actions${query}`, {
      headers: { authorization: `Bearer ${server.token}` },
    });
    return { status: response.status, body: (await response.json()) as HereResponse };
  };

  const all = await get('');
  assert.equal(all.status, 200);
  assert.deepEqual(all.body.scope, { project_id: null, workstream_id: null, execution_id: null });
  assert.deepEqual(types(all.body.actions), ['project.create']);
  const create = all.body.actions[0];
  assert.ok(create?.kind === 'command');
  assert.equal(create.title, 'New Project');
  assert.equal(create.confirm, 'none');
  assert.deepEqual(
    create.needs.map((need) => [need.name, need.kind, need.required]),
    [
      ['name', 'text', true],
      ['location', 'choice', false],
    ],
  );

  const posted = await fetch(`${server.baseUrl}/api/commands`, {
    method: 'POST',
    headers: { authorization: `Bearer ${server.token}`, 'content-type': 'application/json' },
    body: JSON.stringify(server.commands.createProject('Here')),
  });
  assert.equal(posted.status, 202);
  const projectId = server.controlPlane
    .snapshot()
    .projects.find((project) => project.name === 'Here')?.project_id;
  assert.ok(projectId);
  const project = await get(`?project_id=${projectId}`);
  assert.equal(project.status, 200);
  assert.ok(project.body.position > all.body.position, 'the position the list was read at');
  const listed = types(project.body.actions);
  assert.equal(listed[0], 'workstream.create', 'its main action first');
  // No folder: nothing that needs one, and none of the host's parts this test server lacks.
  for (const absent of [
    'project.check',
    'project.show_in_finder',
    'project.restore',
    'project.share.start',
  ])
    assert.ok(!listed.includes(absent), absent);
  assert.deepEqual(listed.slice(-2), ['list:tasks', 'list:versions']);

  // A scope that does not hold together lists nothing; one that is not ids is refused.
  const loose = await get('?workstream_id=01920000-0000-7000-8000-000000000b01');
  assert.deepEqual(loose.body.actions, []);
  assert.equal((await get('?project_id=not-an-id')).status, 400);
  assert.equal((await get('?project_id=01920000-0000-7000-8000-000000000b02&x=1')).status, 400);
});

test('GET /api/actions/choices answers a list by index, its digest, and no id (ADR 0060)', async (t) => {
  const server = await startTestServer();
  t.after(() => server.stop());
  const get = (query: string) =>
    fetch(`${server.baseUrl}/api/actions/choices${query}`, {
      headers: { authorization: `Bearer ${server.token}` },
    });
  const runtimes = await get('?list=runtimes');
  assert.equal(runtimes.status, 200);
  const body = (await runtimes.json()) as ChoicesResponse;
  assert.equal(body.list, 'runtimes');
  assert.ok(body.rows.length > 0 && body.rows.length === body.total);
  assert.deepEqual(
    body.rows.map((row) => row.index),
    body.rows.map((_, index) => index),
  );
  for (const row of body.rows) {
    assert.equal(row.kind, 'runtime');
    assert.deepEqual(row.actions, ['execution.start']);
    assert.ok(!('value' in row), 'the id a row stands for stays the host’s');
  }
  const again = (await (await get('?list=runtimes')).json()) as ChoicesResponse;
  assert.equal(again.digest, body.digest, 'the same rows read the same');
  // Every listed model of every runtime that lists its own, each naming its runtime; the mock lists none.
  const models = await get('?list=models');
  assert.equal(models.status, 200);
  for (const row of ((await models.json()) as ChoicesResponse).rows) {
    assert.equal(row.kind, 'model');
    assert.ok(row.detail !== null, 'each model names its runtime');
  }
  // One app's models, by its row and the apps list's digest as read: a number in the query, the list unchanged.
  const own = await get(`?list=models&runtime=0&runtimes=${body.digest}`);
  assert.equal(own.status, 200);
  assert.ok('default' in ((await own.json()) as ChoicesResponse));
  assert.equal((await get(`?list=models&runtime=0&runtimes=${'f'.repeat(64)}`)).status, 409);
  assert.equal((await get(`?list=models&runtime=zero&runtimes=${body.digest}`)).status, 400);
  assert.equal((await get('?list=models&runtime=0')).status, 400, 'runtime comes with runtimes');
  assert.equal((await get(`?list=folders&runtime=0&runtimes=${body.digest}`)).status, 400);
  // A list this host cannot read by index yet, or one this place does not have.
  assert.equal((await get('?list=catalog')).status, 404);
  assert.equal((await get('?list=versions')).status, 404, 'all work has no versions');
  assert.equal((await get('?list=nothing')).status, 400);
});
