import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import type { ProjectId } from '@halcyonic/contracts';
import { Projection } from './projection.ts';
import { EventBuilder } from './testing/events.ts';

describe('a project begun from a starter', () => {
  test('has no starter until the host journals the one its folder began with', () => {
    const b = new EventBuilder();
    const projection = new Projection();
    const project = b.project('Garden', {
      path: '/projects/garden',
      name: 'garden',
      created: true,
    });
    projection.apply(project.event);
    assert.equal(projection.project(project.projectId)?.starter, null);
    const applied = projection.apply(
      b.controlPlane(
        'project.starter_applied',
        { project_id: project.projectId, workstream_id: null, execution_id: null },
        { starter_id: 'iwsdk-vr', lockfile: 'a'.repeat(64), files: 9, template: 'b'.repeat(64) },
      ),
    );
    assert.equal(projection.project(project.projectId)?.starter, 'iwsdk-vr');
    assert.deepEqual(
      applied.changes.projects.map((view) => view.starter),
      ['iwsdk-vr'],
    );
  });

  test('a starter for a project that does not exist changes nothing and is noted', () => {
    const b = new EventBuilder();
    const projection = new Projection();
    const result = projection.apply(
      b.controlPlane(
        'project.starter_applied',
        {
          project_id: '01920000-0000-7000-8000-0000000000ff' as ProjectId,
          workstream_id: null,
          execution_id: null,
        },
        { starter_id: 'iwsdk-vr', lockfile: 'a'.repeat(64), files: 9, template: 'b'.repeat(64) },
      ),
    );
    assert.equal(result.notes[0]?.code, 'unknown_entity');
  });
});
