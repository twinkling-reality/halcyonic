import assert from 'node:assert/strict';
import { mkdirSync, mkdtempSync, readFileSync, realpathSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { after, describe, type TestContext, test } from 'node:test';
import {
  type CheckpointId,
  type CommandEnvelope,
  type CommandId,
  compileValidator,
  type DeviceId,
  type ExecutionId,
  LocationReviewResponse,
  type Principal,
  type ProjectId,
  type ProjectLocationChoice,
  RestorePreviewResponse,
} from '@halcyonic/contracts';
import { CheckpointStore } from '../checkpoints/store.ts';
import { NO_CAUSE } from '../core/drafts.ts';
import { createHostLocations } from '../locations.ts';
import { pairDevice } from '../testing/device.ts';
import { startTestServer } from '../testing/harness.ts';

const base = realpathSync(mkdtempSync(join(tmpdir(), 'halcyonic-review-')));
after(() => rmSync(base, { recursive: true, force: true }));
const validPreview = compileValidator(RestorePreviewResponse);
const validLocationReview = compileValidator(LocationReviewResponse);

/** A control plane with undo, a project in `app`, a second folder `other`, and a paired device. */
async function serve(t: TestContext, name: string) {
  const root = join(base, name);
  const folder = join(root, 'app');
  mkdirSync(folder, { recursive: true });
  mkdirSync(join(root, 'other'), { recursive: true });
  writeFileSync(join(folder, 'app.txt'), 'person\n');
  const server = await startTestServer({
    network: {},
    locations: createHostLocations([root]),
    checkpoints: {
      store: new CheckpointStore({ git: '/usr/bin/git', directory: join(root, 'checkpoints') }),
    },
  });
  t.after(() => server.stop());
  const created = server.controlPlane.commands.submit(
    server.commands.createProject('Garden', { kind: 'existing_folder', root, folder_name: 'app' }),
    'internal',
  );
  const result = created.command?.result;
  const projectId = (result?.kind === 'project_created' ? result.project_id : '') as ProjectId;
  const opened = await fetch(`${server.baseUrl}/api/pairing`, {
    method: 'POST',
    headers: { authorization: `Bearer ${server.token}` },
  });
  const { code } = (await opened.json()) as { code: string };
  assert.ok(server.network);
  const paired = await pairDevice(server.network.target, code);
  const device: Principal = { kind: 'device', device_id: paired.deviceId as DeviceId };
  const local: Principal = { kind: 'local' };
  const read = async (method: string, path: string, body?: unknown) => {
    const response = await fetch(`${server.baseUrl}${path}`, {
      method,
      headers: {
        authorization: `Bearer ${server.token}`,
        ...(body === undefined ? {} : { 'content-type': 'application/json' }),
      },
      ...(body === undefined ? {} : { body: JSON.stringify(body) }),
    });
    return { status: response.status, body: (await response.json()) as unknown };
  };
  return { server, root, folder, projectId, device, local, read };
}

let next = 0;
function restore(projectId: ProjectId, checkpointId: string, review?: string): CommandEnvelope {
  next += 1;
  return {
    schema_version: 1,
    command_id: `01920000-0000-7000-8000-${String(next).padStart(12, '0')}` as CommandId,
    issued_at: '2026-09-26T10:00:00.000Z',
    client: { name: 'test', version: null, device_label: null },
    command_type: 'project.restore',
    payload: {
      project_id: projectId,
      checkpoint_id: checkpointId as CheckpointId,
      ...(review === undefined ? {} : { review }),
    },
  };
}

async function settled(server: Awaited<ReturnType<typeof serve>>['server'], commandId: string) {
  for (let tries = 0; tries < 200; tries += 1) {
    const view = server.controlPlane.projection.command(commandId);
    if (view !== undefined && view.status !== 'accepted') return view;
    await new Promise((resolve) => setTimeout(resolve, 10));
  }
  throw new Error('the command did not settle');
}

describe('a review a command carries back (ADR 0051)', () => {
  test('the restore preview answers the digest of what it shows, which changes with the files', async (t) => {
    const { server, folder, projectId, read } = await serve(t, 'preview');
    const checkpoints = server.controlPlane.checkpoints;
    assert.ok(checkpoints);
    await checkpoints.beforeTask(
      projectId,
      '01920000-0000-7000-8000-0000000000aa' as ExecutionId,
      folder,
      NO_CAUSE,
    );
    const checkpointId = checkpoints.checkpoints(projectId)[0]?.checkpoint_id ?? '';
    writeFileSync(join(folder, 'app.txt'), 'agent\n');
    const path = `/api/projects/${projectId}/checkpoints/${checkpointId}/restore-preview`;
    const first = await read('GET', path);
    assert.ok(validPreview(first.body).ok);
    const result = (first.body as RestorePreviewResponse).result;
    assert.equal(result.availability, 'available');
    const review = result.availability === 'available' ? result.review : '';
    assert.match(review, /^[0-9a-f]{64}$/);
    assert.equal(
      ((await read('GET', path)).body as RestorePreviewResponse).result.availability ===
        'available' &&
        ((await read('GET', path)).body as { result: { review: string } }).result.review,
      review,
      'the same files, the same digest',
    );
    writeFileSync(join(folder, 'extra.txt'), 'new\n');
    const changed = (await read('GET', path)).body as { result: { review: string } };
    assert.notEqual(changed.result.review, review, 'what a restore would change has changed');
  });

  test('the digest covers every path a restore would change, past the ones the preview lists', async (t) => {
    const { server, folder, projectId, read } = await serve(t, 'many');
    const checkpoints = server.controlPlane.checkpoints;
    assert.ok(checkpoints);
    await checkpoints.beforeTask(
      projectId,
      '01920000-0000-7000-8000-0000000000aa' as ExecutionId,
      folder,
      NO_CAUSE,
    );
    const checkpointId = checkpoints.checkpoints(projectId)[0]?.checkpoint_id ?? '';
    for (let n = 0; n < 105; n += 1) {
      writeFileSync(join(folder, `extra-${String(n).padStart(3, '0')}.txt`), 'new\n');
    }
    const path = `/api/projects/${projectId}/checkpoints/${checkpointId}/restore-preview`;
    const before = (await read('GET', path)).body as RestorePreviewResponse;
    assert.equal(before.result.availability, 'available');
    if (before.result.availability !== 'available') return;
    assert.equal(before.result.preview.removed.total, 105);
    assert.ok(before.result.preview.removed.paths.length < 105, 'the preview lists only the first');
    // One unlisted file swapped for another of the same count: the list shown is the same.
    rmSync(join(folder, 'extra-104.txt'));
    writeFileSync(join(folder, 'extra-999.txt'), 'new\n');
    const after = (await read('GET', path)).body as RestorePreviewResponse;
    if (after.result.availability !== 'available') return;
    assert.deepEqual(
      after.result.preview,
      before.result.preview,
      'what the person saw is the same',
    );
    assert.notEqual(
      after.result.review,
      before.result.review,
      'what a restore would remove is not',
    );
  });

  test('a device restores only with the digest of the preview it read, and a stale one changes nothing', async (t) => {
    const { server, folder, projectId, device, local, read } = await serve(t, 'restore');
    const checkpoints = server.controlPlane.checkpoints;
    assert.ok(checkpoints);
    await checkpoints.beforeTask(
      projectId,
      '01920000-0000-7000-8000-0000000000aa' as ExecutionId,
      folder,
      NO_CAUSE,
    );
    const checkpointId = checkpoints.checkpoints(projectId)[0]?.checkpoint_id ?? '';
    writeFileSync(join(folder, 'app.txt'), 'agent\n');
    const path = `/api/projects/${projectId}/checkpoints/${checkpointId}/restore-preview`;
    const reviewNow = async () =>
      ((await read('GET', path)).body as { result: { review: string } }).result.review;

    const bare = server.controlPlane.commands.submit(
      restore(projectId, checkpointId),
      'http',
      device,
    );
    assert.equal(bare.disposition, 'rejected');
    assert.equal(bare.command?.rejection?.code, 'review_required');

    const read1 = await reviewNow();
    // A change the preview shows: a new file a restore would now remove. (Writing app.txt again would
    // not be one: the preview names the files it changes back, not what they hold.)
    writeFileSync(join(folder, 'late.txt'), 'late\n');
    const stale = server.controlPlane.commands.submit(
      restore(projectId, checkpointId, read1),
      'http',
      device,
    );
    assert.equal(stale.disposition, 'accepted');
    const staleView = await settled(server, stale.command?.command_id ?? '');
    assert.equal(staleView.status, 'failed');
    assert.equal(staleView.failure?.code, 'review_stale');
    assert.equal(readFileSync(join(folder, 'app.txt'), 'utf8'), 'agent\n', 'nothing was written');
    assert.equal(readFileSync(join(folder, 'late.txt'), 'utf8'), 'late\n');

    const fresh = server.controlPlane.commands.submit(
      restore(projectId, checkpointId, await reviewNow()),
      'http',
      device,
    );
    assert.equal(fresh.disposition, 'accepted');
    assert.equal((await settled(server, fresh.command?.command_id ?? '')).status, 'completed');
    assert.equal(readFileSync(join(folder, 'app.txt'), 'utf8'), 'person\n');
    const accepted = [...server.journal.readAll()].find(
      (stored) =>
        stored.event.event_type === 'command.accepted' &&
        stored.event.payload.command.command_id === fresh.command?.command_id,
    );
    assert.ok(accepted, 'the digest is journaled with the accepted command');

    // The owner on the Mac is admitted as before.
    const owner = server.controlPlane.commands.submit(
      restore(projectId, checkpointId),
      'http',
      local,
    );
    assert.equal(owner.disposition, 'accepted');
    await settled(server, owner.command?.command_id ?? '');
  });

  test('a device moves a project only with the review of that move, while it still holds', async (t) => {
    const { server, root, projectId, device, local, read } = await serve(t, 'move');
    const other: ProjectLocationChoice = { kind: 'existing_folder', root, folder_name: 'other' };
    const back: ProjectLocationChoice = { kind: 'existing_folder', root, folder_name: 'app' };
    const move = (location: ProjectLocationChoice, review?: string) => {
      const command = server.commands.setLocation(projectId, location);
      return review === undefined
        ? command
        : { ...command, payload: { ...command.payload, review } };
    };

    const bare = server.controlPlane.commands.submit(move(other), 'http', device);
    assert.equal(bare.command?.rejection?.code, 'review_required');

    const reviewed = await read('POST', `/api/projects/${projectId}/location-review`, {
      location: other,
    });
    assert.equal(reviewed.status, 200);
    assert.ok(validLocationReview(reviewed.body).ok);
    const answer = reviewed.body as LocationReviewResponse;
    assert.equal(answer.current?.name, 'app');
    assert.deepEqual(answer.next, other);

    // Someone moves it meanwhile: the review the person read no longer holds.
    const ownerMove = server.controlPlane.commands.submit(move(other), 'http', local);
    assert.equal(ownerMove.disposition, 'accepted', 'the owner on the Mac needs no digest');
    const stale = server.controlPlane.commands.submit(move(other, answer.review), 'http', device);
    assert.equal(stale.command?.rejection?.code, 'review_stale');

    const again = (
      await read('POST', `/api/projects/${projectId}/location-review`, { location: back })
    ).body as LocationReviewResponse;
    const moved = server.controlPlane.commands.submit(move(back, again.review), 'http', device);
    assert.equal(moved.disposition, 'accepted');
    const wrong = server.controlPlane.commands.submit(move(other, again.review), 'http', local);
    assert.equal(
      wrong.command?.rejection?.code,
      'review_stale',
      'a digest sent is checked, whoever sends it',
    );
  });

  test('a first folder for a project needs no review; unknown projects and bad bodies are refused', async (t) => {
    const { server, root, device, read } = await serve(t, 'first');
    const created = server.controlPlane.commands.submit(
      server.commands.createProject('Bare'),
      'internal',
    );
    const result = created.command?.result;
    const bareId = (result?.kind === 'project_created' ? result.project_id : '') as ProjectId;
    const first = server.controlPlane.commands.submit(
      server.commands.setLocation(bareId, { kind: 'existing_folder', root, folder_name: 'other' }),
      'http',
      device,
    );
    assert.equal(first.disposition, 'accepted', 'nothing was there to lose');
    assert.equal(
      (
        await read('POST', '/api/projects/01920000-0000-7000-8000-0000000000ff/location-review', {
          location: { kind: 'existing_folder', root, folder_name: 'other' },
        })
      ).status,
      404,
    );
    assert.equal(
      (await read('POST', `/api/projects/${bareId}/location-review`, { location: 3 })).status,
      400,
    );
  });
});
