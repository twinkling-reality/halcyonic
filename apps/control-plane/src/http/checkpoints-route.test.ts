import assert from 'node:assert/strict';
import { mkdirSync, mkdtempSync, realpathSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { after, describe, test } from 'node:test';
import {
  CheckpointsResponse,
  compileValidator,
  type ExecutionId,
  type ProjectId,
  RestorePreviewResponse,
} from '@halcyonic/contracts';
import { CheckpointStore } from '../checkpoints/store.ts';
import { NO_CAUSE } from '../core/drafts.ts';
import { createHostLocations } from '../locations.ts';
import { startTestServer } from '../testing/harness.ts';

const base = realpathSync(mkdtempSync(join(tmpdir(), 'halcyonic-checkpoints-route-')));
after(() => rmSync(base, { recursive: true, force: true }));

const validCheckpoints = compileValidator(CheckpointsResponse);
const validPreview = compileValidator(RestorePreviewResponse);

async function serve(name: string, undo: boolean) {
  const root = join(base, name);
  const folder = join(root, 'app');
  mkdirSync(folder, { recursive: true });
  writeFileSync(join(folder, 'app.txt'), 'person\n');
  const server = await startTestServer({
    locations: createHostLocations([root]),
    ...(undo && {
      checkpoints: {
        store: new CheckpointStore({ git: '/usr/bin/git', directory: join(root, 'checkpoints') }),
      },
    }),
  });
  const created = server.controlPlane.commands.submit(
    server.commands.createProject('Garden', {
      kind: 'existing_folder',
      root,
      folder_name: 'app',
    }),
    'internal',
  );
  const result = created.command?.result;
  const projectId = (result?.kind === 'project_created' ? result.project_id : '') as ProjectId;
  const get = async (path: string, token: string | null = server.token) => {
    const response = await fetch(`${server.baseUrl}${path}`, {
      headers: token === null ? {} : { authorization: `Bearer ${token}` },
    });
    return { status: response.status, body: (await response.json()) as unknown };
  };
  return { server, folder, projectId, get };
}

describe('reading checkpoints over HTTP', () => {
  test('with undo off, both reads say it is not set up', async () => {
    const { server, projectId, get } = await serve('off', false);
    const list = await get(`/api/projects/${projectId}/checkpoints`);
    assert.equal(list.status, 200);
    assert.ok(validCheckpoints(list.body).ok);
    assert.deepEqual((list.body as { result: unknown }).result, {
      availability: 'unavailable',
      reason: {
        code: 'undo_not_set_up',
        message: 'Undo is not set up on this computer (HALCYONIC_GIT_BIN).',
      },
    });
    const preview = await get(
      `/api/projects/${projectId}/checkpoints/01920000-0000-7000-8000-000000000001/restore-preview`,
    );
    assert.equal(preview.status, 200);
    assert.ok(validPreview(preview.body).ok);
    await server.stop();
  });

  test('lists the project’s checkpoints and previews what a restore would change', async () => {
    const { server, folder, projectId, get } = await serve('on', true);
    const checkpoints = server.controlPlane.checkpoints;
    assert.ok(checkpoints);
    await checkpoints.beforeTask(
      projectId,
      '01920000-0000-7000-8000-0000000000aa' as ExecutionId,
      folder,
      NO_CAUSE,
    );
    const list = await get(`/api/projects/${projectId}/checkpoints`);
    assert.equal(list.status, 200);
    assert.ok(validCheckpoints(list.body).ok);
    const result = (list.body as CheckpointsResponse).result;
    assert.equal(result.availability, 'available');
    const checkpointId =
      result.availability === 'available' ? result.checkpoints[0]?.checkpoint_id : '';
    assert.ok(checkpointId);

    writeFileSync(join(folder, 'app.txt'), 'agent\n');
    writeFileSync(join(folder, 'extra.txt'), 'new\n');
    const preview = await get(
      `/api/projects/${projectId}/checkpoints/${checkpointId}/restore-preview`,
    );
    assert.equal(preview.status, 200);
    assert.ok(validPreview(preview.body).ok);
    assert.deepEqual((preview.body as RestorePreviewResponse).result, {
      availability: 'available',
      preview: {
        checkpoint_id: checkpointId,
        changed_back: { paths: ['app.txt'], total: 1 },
        removed: { paths: ['extra.txt'], total: 1 },
        returned: { paths: [], total: 0 },
        project_head_moved: null,
      },
    });
    // A preview changes nothing, and is never journaled.
    assert.equal(
      [...server.journal.readAll()].some(
        (stored) => stored.event.event_type === 'project.restored',
      ),
      false,
    );
    await server.stop();
  });

  test('refuses malformed ids, unknown projects and checkpoints, and anyone without the token', async () => {
    const { server, projectId, get } = await serve('refusals', true);
    assert.equal((await get('/api/projects/not-an-id/checkpoints')).status, 400);
    assert.equal(
      (await get('/api/projects/01920000-0000-7000-8000-0000000000ff/checkpoints')).status,
      404,
    );
    assert.equal(
      (
        await get(
          `/api/projects/${projectId}/checkpoints/01920000-0000-7000-8000-000000000001/restore-preview`,
        )
      ).status,
      404,
    );
    assert.equal((await get(`/api/projects/${projectId}/checkpoints`, null)).status, 401);
    await server.stop();
  });
});
