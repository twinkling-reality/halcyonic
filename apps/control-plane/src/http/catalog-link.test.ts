import assert from 'node:assert/strict';
import { randomUUID } from 'node:crypto';
import { after, before, describe, test } from 'node:test';
import type { EventEnvelope } from '@halcyonic/contracts';
import { startTestServer, TEST_CLIENT } from '../testing/harness.ts';

let server: Awaited<ReturnType<typeof startTestServer>>;
before(async () => {
  server = await startTestServer();
});
after(() => server.stop());

async function post(body: unknown) {
  const response = await fetch(`${server.baseUrl}/api/commands`, {
    method: 'POST',
    headers: { authorization: `Bearer ${server.token}`, 'content-type': 'application/json' },
    body: JSON.stringify(body),
  });
  return { status: response.status, body: (await response.json()) as Record<string, unknown> };
}

function command(commandType: string, payload: Record<string, unknown>) {
  return {
    schema_version: 1,
    command_id: randomUUID(),
    command_type: commandType,
    issued_at: '2026-10-29T10:00:00.000Z',
    client: TEST_CLIENT,
    payload,
  };
}

function rejection(body: Record<string, unknown>): string | undefined {
  return (body.command as { rejection: { code: string } | null }).rejection?.code;
}

function events(type: string): EventEnvelope[] {
  return [...server.journal.readAll()]
    .map((stored) => stored.event)
    .filter((event) => event.event_type === type);
}

const app = { source: 'phat-porcupine', repository_id: 7, full_name: 'acme/app' };
const api = { source: 'phat-porcupine', repository_id: 8, full_name: 'acme/app-api' };
const LINK_COMMANDS = ['project.link_catalog', 'project.unlink_catalog'];

describe('linking a project to a catalog repository (ADR 0039)', () => {
  test('the person links, relinks and unlinks; each is journaled as the control plane saw it', async () => {
    const created = await post(server.commands.createProject('Catalog'));
    assert.equal(created.status, 202);
    const projectId = server.controlPlane
      .snapshot()
      .projects.find((project) => project.name === 'Catalog')?.project_id;
    assert.ok(projectId);

    for (const catalog of [app, api]) {
      const linked = await post(
        command('project.link_catalog', { project_id: projectId, catalog }),
      );
      assert.equal(linked.status, 202);
      assert.equal(linked.body.disposition, 'accepted');
    }
    const unlinked = await post(command('project.unlink_catalog', { project_id: projectId }));
    assert.equal(unlinked.status, 202);

    assert.deepEqual(
      events('project.catalog_linked').map((event) => event.payload),
      [
        { catalog: app, previous: null },
        { catalog: api, previous: app },
      ],
    );
    assert.deepEqual(
      events('project.catalog_unlinked').map((event) => event.payload),
      [{ previous: api }],
    );
    for (const event of [
      ...events('project.catalog_linked'),
      ...events('project.catalog_unlinked'),
    ]) {
      assert.equal(event.project_id, projectId);
      assert.equal(event.provenance.epistemic, 'observed', "the fact is the person's command");
    }
    const completed = events('command.completed').filter((event) =>
      LINK_COMMANDS.includes((event.payload as { command_type: string }).command_type),
    );
    assert.equal(completed.length, 3, 'all three completed at once, as local commands do');
  });

  test('refusals create nothing: an unknown project, and an unlink with no link', async () => {
    const before =
      events('project.catalog_linked').length + events('project.catalog_unlinked').length;
    const unknown = await post(
      command('project.link_catalog', {
        project_id: '01920000-0000-7000-8000-00000000ffff',
        catalog: app,
      }),
    );
    assert.equal(unknown.body.disposition, 'rejected');
    assert.equal(rejection(unknown.body), 'project_not_found');

    await post(server.commands.createProject('Unlinked'));
    const projectId = server.controlPlane
      .snapshot()
      .projects.find((project) => project.name === 'Unlinked')?.project_id;
    const notLinked = await post(command('project.unlink_catalog', { project_id: projectId }));
    assert.equal(notLinked.body.disposition, 'rejected');
    assert.equal(rejection(notLinked.body), 'invalid_state');

    const url = await post(
      command('project.link_catalog', {
        project_id: projectId,
        catalog: { ...app, full_name: 'https://token@github.com/acme/app' },
      }),
    );
    assert.equal(url.status, 400, 'a URL never passes as owner/name');
    assert.equal(
      events('project.catalog_linked').length + events('project.catalog_unlinked').length,
      before,
    );
  });
});
