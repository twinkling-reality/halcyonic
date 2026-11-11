import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import { type CommandEnvelope, type CommandId, MAX_PROJECT_SECRETS } from '@halcyonic/contracts';
import { admitCommand } from './admission.ts';
import { Projection } from './projection.ts';
import { reservedSecretName } from './secrets.ts';
import { EventBuilder } from './testing/events.ts';

// A project's secrets in current state (ADR 0057): names only, sorted, from the journal.

const catalog = { descriptor: () => undefined, descriptors: () => [] } as never;

function made() {
  const b = new EventBuilder();
  const projection = new Projection();
  const project = b.project('Garden', { path: '/work/garden', name: 'garden', created: true });
  projection.apply(project.event);
  const scope = { project_id: project.projectId, workstream_id: null, execution_id: null };
  const store = (name: string, replaced = false) =>
    projection.apply(b.controlPlane('project.secret.stored', scope, { name, replaced }));
  const remove = (name: string) =>
    projection.apply(b.controlPlane('project.secret.removed', scope, { name }));
  return { projection, projectId: project.projectId, store, remove };
}

const command = (
  command_type: 'project.secret.set' | 'project.secret.remove',
  projectId: string,
  name: string,
): CommandEnvelope =>
  ({
    schema_version: 1,
    command_id: '00000000-0000-4000-9000-000000000057' as CommandId,
    issued_at: '2026-11-11T10:00:00.000Z',
    client: { name: 'test', version: null, device_label: null },
    command_type,
    payload: { project_id: projectId, name },
  }) as CommandEnvelope;

describe("a project's secrets in current state (ADR 0057)", () => {
  test('names only, sorted, once each; a removal takes one out, and none left means none listed', () => {
    const { projection, projectId, store, remove } = made();
    assert.equal(projection.project(projectId)?.secrets, undefined);
    assert.deepEqual(
      store('ZETA_KEY').changes.projects.map((view) => view.project_id),
      [projectId],
    );
    store('ALPHA_KEY');
    store('ZETA_KEY', true);
    assert.deepEqual(projection.project(projectId)?.secrets, ['ALPHA_KEY', 'ZETA_KEY']);
    remove('ZETA_KEY');
    assert.deepEqual(projection.project(projectId)?.secrets, ['ALPHA_KEY']);
    remove('ALPHA_KEY');
    assert.equal(projection.project(projectId)?.secrets, undefined);
  });

  test('they survive a checkpoint of the projection (ADR 0050)', () => {
    const { projection, projectId, store } = made();
    store('API_KEY');
    const restored = new Projection();
    restored.restoreState(projection.exportState());
    assert.deepEqual(restored.project(projectId)?.secrets, ['API_KEY']);
  });
});

describe('admitting a secret set or removal (ADR 0057)', () => {
  test('a set needs the project and a name the Mac and its agents do not use, within the cap', () => {
    const { projection, projectId, store } = made();
    assert.equal(
      admitCommand(command('project.secret.set', projectId, 'API_KEY'), projection, catalog)
        .admitted,
      true,
    );
    const missing = admitCommand(
      command('project.secret.set', '00000000-0000-7000-8000-000000000999', 'API_KEY'),
      projection,
      catalog,
    );
    assert.equal(!missing.admitted && missing.rejection.code, 'project_not_found');
    for (const name of [
      'PATH',
      'HOME',
      'OPENCODE_PASSWORD',
      'DYLD_INSERT_LIBRARIES',
      'NODE_OPTIONS',
      'GIT_DIR',
    ]) {
      const refused = admitCommand(
        command('project.secret.set', projectId, name),
        projection,
        catalog,
      );
      assert.equal(!refused.admitted && refused.rejection.code, 'invalid_state', name);
    }
    for (let at = 0; at < MAX_PROJECT_SECRETS; at += 1) store(`KEY_${at}`);
    const full = admitCommand(
      command('project.secret.set', projectId, 'ONE_MORE'),
      projection,
      catalog,
    );
    assert.equal(
      !full.admitted && full.rejection.message,
      `A project keeps at most ${MAX_PROJECT_SECRETS} secrets. Remove one first.`,
    );
    // Replacing one it has is still allowed at the cap.
    assert.equal(
      admitCommand(command('project.secret.set', projectId, 'KEY_0'), projection, catalog).admitted,
      true,
    );
  });

  test('a removal needs a secret of that name', () => {
    const { projection, projectId, store } = made();
    const none = admitCommand(
      command('project.secret.remove', projectId, 'API_KEY'),
      projection,
      catalog,
    );
    assert.equal(
      !none.admitted && none.rejection.message,
      'This project has no secret named API_KEY.',
    );
    store('API_KEY');
    assert.equal(
      admitCommand(command('project.secret.remove', projectId, 'API_KEY'), projection, catalog)
        .admitted,
      true,
    );
  });

  test('ordinary key names are free', () => {
    for (const name of [
      'API_KEY',
      'STRIPE_TEST_KEY',
      'SUPABASE_URL',
      'OPENAI_API_KEY',
      'GITHUB_TOKEN',
    ])
      assert.equal(reservedSecretName(name), null, name);
  });
});
