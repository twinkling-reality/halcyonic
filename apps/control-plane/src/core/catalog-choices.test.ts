import assert from 'node:assert/strict';
import { randomUUID } from 'node:crypto';
import { test } from 'node:test';
import type { CatalogProject, CatalogResponse, CommandId, HereScope } from '@halcyonic/contracts';
import { createTestControlPlane, TEST_CLIENT } from '../testing/harness.ts';

function listed(name: string, repositories: [number, string, boolean][]): CatalogProject {
  return {
    catalog_id: name.toLowerCase(),
    name,
    group_id: null,
    group_order: null,
    repositories: repositories.map(([repository_id, full_name, archived], index) => ({
      repository_id,
      full_name,
      primary: index === 0,
      visibility: 'private',
      fork: false,
      archived,
      language: null,
      description: null,
      pushed_at: null,
      access: 'ok',
    })),
    state: {
      state: 'active',
      active: true,
      last_activity_at: null,
      sessions: { running: 0, awaiting_input: 0 },
      unknown: [],
    },
    attention: [],
    local: { project_ids: [], suggestions: [] },
  } as CatalogProject;
}

function catalogOf(sample: boolean, projects: CatalogProject[]): CatalogResponse {
  return {
    availability: 'available',
    provenance: 'reported',
    source: { system: 'omnorium', api_version: '1', synthetic: false, sample },
    generated_at: '2026-11-12T13:00:00Z',
    sync: {
      repositories_at: null,
      repositories_failed: false,
      activity_at: null,
      activity_failed: false,
    },
    groups: [],
    projects,
    truncated: null,
  } as CatalogResponse;
}

/** A catalog source as the host's: what it read last is what a link may name, never a sample's. */
function catalogLinks(answer: () => CatalogResponse) {
  let last: CatalogResponse | null = null;
  return {
    read: async () => {
      last = answer();
      return last;
    },
    check: (ref: { repository_id: number; full_name: string }) => {
      if (last === null || last.availability !== 'available')
        return { ok: false as const, message: 'No catalog has been read yet.' };
      if (last.source.sample)
        return {
          ok: false as const,
          message: 'A sample catalog is never linked to your projects.',
        };
      const known = last.projects.some((p) =>
        p.repositories.some(
          (r) => r.repository_id === ref.repository_id && r.full_name === ref.full_name,
        ),
      );
      return known ? { ok: true as const } : { ok: false as const, message: 'Not in the catalog.' };
    },
  };
}

test('a project is linked to a catalog repository by its place in the list read now, archived ones and a sample’s never offered', async () => {
  let sample = false;
  const { controlPlane, commands } = createTestControlPlane({
    catalogLinks: catalogLinks(() =>
      catalogOf(sample, [
        listed('Lanterns', [
          [7, 'acme/lanterns', false],
          [9, 'acme/lanterns-old', true],
        ]),
        listed('Chimes', [[8, 'acme/chimes', false]]),
      ]),
    ) as never,
  });
  const made = controlPlane.commands.submit(commands.createProject('Lanterns'), 'internal');
  const projectId =
    made.command?.result?.kind === 'project_created' ? made.command.result.project_id : '';
  const scope = { project_id: projectId, workstream_id: null, execution_id: null } as HereScope;
  const list = await controlPlane.commands.choices(scope, 'catalog');
  assert.ok(list !== null);
  assert.deepEqual(
    list.rows.map((r) => [r.index, r.kind, r.label, r.detail, r.actions]),
    [
      [0, 'repository', 'acme/lanterns', 'Lanterns', ['project.link_catalog']],
      [1, 'repository', 'acme/chimes', 'Chimes', ['project.link_catalog']],
    ],
  );
  const prepared = await controlPlane.commands.prepare({
    scope,
    command_type: 'project.link_catalog',
    values: { catalog: 0 },
    read: { catalog: list.digest },
    command_id: randomUUID() as CommandId,
    issued_at: '2026-11-12T13:55:00.000Z',
    client: TEST_CLIENT,
  });
  assert.ok(prepared.ok, prepared.ok ? '' : prepared.message);
  assert.deepEqual(prepared.command.payload, {
    project_id: projectId,
    catalog: { source: 'omnorium', repository_id: 7, full_name: 'acme/lanterns' },
  });
  const linked = controlPlane.commands.submit(prepared.command, 'internal');
  assert.equal(linked.command?.rejection ?? null, null, 'the prepared link is admitted whole');
  // A sample catalog is shown, never linked: its rows offer nothing and say why.
  sample = true;
  const shown = await controlPlane.commands.choices(scope, 'catalog');
  assert.ok(shown !== null);
  for (const row of shown.rows) {
    assert.deepEqual(row.actions, []);
    assert.match(row.detail ?? '', /sample catalog/);
  }
});

test('with no catalog set up, the catalog is no list', async () => {
  const { controlPlane, commands } = createTestControlPlane({});
  const made = controlPlane.commands.submit(commands.createProject('Alone'), 'internal');
  const projectId =
    made.command?.result?.kind === 'project_created' ? made.command.result.project_id : '';
  const scope = { project_id: projectId, workstream_id: null, execution_id: null } as HereScope;
  assert.equal(await controlPlane.commands.choices(scope, 'catalog'), null);
});
