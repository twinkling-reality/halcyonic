import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { describe, test } from 'node:test';
import type { CommandEnvelope, CommandId, WorldDescription } from '@halcyonic/contracts';
import { admitCommand } from './admission.ts';
import { Projection } from './projection.ts';
import { EventBuilder } from './testing/events.ts';

const starting = JSON.parse(
  readFileSync(new URL('../../../starters/world/template/world.json', import.meta.url), 'utf8'),
) as WorldDescription;
const night: WorldDescription = { ...starting, stars: 0.9 };
const refused = {
  code: 'not_json',
  where: null,
  message: 'world.json is not JSON.',
} as const;

/** A project made from a starter, in a projection. */
function made(starter: string) {
  const b = new EventBuilder();
  const projection = new Projection();
  const project = b.project('My World', { path: '/work/world', name: 'world', created: true });
  projection.apply(project.event);
  const scope = { project_id: project.projectId, workstream_id: null, execution_id: null };
  projection.apply(
    b.controlPlane('project.starter_applied', scope, {
      starter_id: starter,
      lockfile: 'a'.repeat(64),
      files: 2,
      template: 'b'.repeat(64),
    }),
  );
  return { b, projection, scope, projectId: project.projectId };
}

describe("a world project's description in current state (ADR 0049)", () => {
  test('a project has no world until the Mac has read one, then the newest valid one, with when', () => {
    const { b, projection, scope, projectId } = made('world');
    assert.equal(projection.project(projectId)?.world, undefined);
    assert.equal(projection.world(projectId), undefined);
    const first = b.controlPlane('project.world.observed', scope, {
      description: starting,
      refused: null,
    });
    assert.deepEqual(
      projection.apply(first).changes.projects.map((view) => view.project_id),
      [projectId],
    );
    assert.deepEqual(projection.project(projectId)?.world, {
      description: starting,
      refused: null,
      observed_at: first.event.occurred_at,
    });
    projection.apply(
      b.controlPlane('project.world.observed', scope, { description: night, refused: null }),
    );
    assert.deepEqual(projection.world(projectId)?.description, night);
  });

  test('a refused reading keeps the last valid description and says why; a valid one clears the why', () => {
    const { b, projection, scope, projectId } = made('world');
    projection.apply(
      b.controlPlane('project.world.observed', scope, { description: null, refused }),
    );
    assert.deepEqual(projection.world(projectId)?.description, null);
    assert.deepEqual(projection.world(projectId)?.refused, refused);
    projection.apply(
      b.controlPlane('project.world.observed', scope, { description: night, refused: null }),
    );
    projection.apply(
      b.controlPlane('project.world.observed', scope, { description: null, refused }),
    );
    assert.deepEqual(projection.world(projectId)?.description, night);
    assert.deepEqual(projection.world(projectId)?.refused, refused);
    projection.apply(
      b.controlPlane('project.world.observed', scope, { description: starting, refused: null }),
    );
    assert.deepEqual(projection.project(projectId)?.world?.description, starting);
    assert.equal(projection.project(projectId)?.world?.refused, null);
  });

  test('a reading for a project that does not exist is noted and changes nothing', () => {
    const { b, projection } = made('world');
    const unknown = { project_id: b.id() as never, workstream_id: null, execution_id: null };
    const applied = projection.apply(
      b.controlPlane('project.world.observed', unknown, { description: starting, refused: null }),
    );
    assert.equal(applied.notes[0]?.code, 'unknown_entity');
    assert.equal(projection.world(unknown.project_id), undefined);
  });
});

describe('checking a world', () => {
  const check = (projectId: string): CommandEnvelope =>
    ({
      schema_version: 1,
      command_id: '00000000-0000-4000-9000-000000000001' as CommandId,
      issued_at: '2026-11-10T10:00:00.000Z',
      client: { name: 'test', version: null, device_label: null },
      command_type: 'project.check',
      payload: { project_id: projectId },
    }) as CommandEnvelope;
  const catalog = { descriptor: () => undefined, descriptors: () => [] } as never;

  test('a world has no build, so its check is refused, while another starter project is still checked', () => {
    const world = made('world');
    const refusedCheck = admitCommand(check(world.projectId), world.projection, catalog);
    assert.equal(refusedCheck.admitted, false);
    assert.equal(!refusedCheck.admitted && refusedCheck.rejection.code, 'invalid_state');
    assert.equal(
      !refusedCheck.admitted && refusedCheck.rejection.message,
      'A world has no build to check.',
    );
    const app = made('iwsdk-vr');
    assert.equal(admitCommand(check(app.projectId), app.projection, catalog).admitted, true);
  });
});
