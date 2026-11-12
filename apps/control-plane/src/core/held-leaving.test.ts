import assert from 'node:assert/strict';
import { randomUUID } from 'node:crypto';
import { mkdirSync, mkdtempSync, readdirSync, realpathSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { after, test } from 'node:test';
import type { CommandEnvelope, CommandId, ProjectId } from '@halcyonic/contracts';
import { CheckpointStore } from '../checkpoints/store.ts';
import { createHostLocations } from '../locations.ts';
import { createTestControlPlane, TEST_CLIENT } from '../testing/harness.ts';

// Through the control plane (the lead, 2026-11-12): what the host holds as secrets, the list redaction
// reads, is what Save a Copy searches its copy for. A made-up value only; nothing real is used.

const base = realpathSync(mkdtempSync(join(tmpdir(), 'halcyonic-held-leaving-')));
after(() => rmSync(base, { recursive: true, force: true }));

async function until(check: () => boolean, what: string): Promise<void> {
  const end = Date.now() + 15_000;
  while (!check()) {
    if (Date.now() > end) throw new Error(`timed out waiting for ${what}`);
    await new Promise((resolve) => setTimeout(resolve, 20));
  }
}

test('Save a Copy of a project whose files hold a secret the host keeps fails in words naming the file, never the value', {
  skip: process.platform !== 'darwin',
}, async () => {
  const value = 'host-held-value-0123456789abcdef';
  const projects = join(base, 'projects');
  const downloads = join(base, 'Downloads');
  mkdirSync(join(projects, 'garden'), { recursive: true });
  mkdirSync(downloads);
  writeFileSync(join(projects, 'garden', 'index.html'), '<title>Garden</title>\n');
  writeFileSync(join(projects, 'garden', 'settings.json'), JSON.stringify({ key: value }));
  const { controlPlane, commands } = createTestControlPlane({
    locations: createHostLocations([projects]),
    secrets: () => [{ what: 'Service key', value }],
    checkpoints: {
      store: new CheckpointStore({
        git: '/usr/bin/git',
        directory: join(base, 'data', 'checkpoints'),
      }),
      keeping: { downloads, reveal: async () => true },
    },
  });
  const created = controlPlane.commands.submit(
    commands.createProject('Garden', {
      kind: 'existing_folder',
      root: projects,
      folder_name: 'garden',
    }),
    'internal',
  );
  const projectId = (
    created.command?.result?.kind === 'project_created' ? created.command.result.project_id : ''
  ) as ProjectId;
  const command = {
    schema_version: 1,
    command_id: randomUUID() as CommandId,
    issued_at: '2026-11-12T15:00:00.000Z',
    client: TEST_CLIENT,
    command_type: 'project.save_copy',
    payload: { project_id: projectId },
  } as CommandEnvelope;
  assert.equal(controlPlane.commands.submit(command, 'internal').disposition, 'accepted');
  await until(() => {
    const status = controlPlane.projection.command(command.command_id)?.status;
    return status === 'completed' || status === 'failed';
  }, 'Save a Copy');
  const done = controlPlane.projection.command(command.command_id);
  assert.equal(done?.status, 'failed');
  assert.equal(done?.failure?.code, 'holds_secret');
  assert.equal(done?.failure?.effect, 'none');
  assert.equal(
    done?.failure?.message,
    'settings.json holds a secret this Mac keeps (Service key), so no copy was saved. Take it out of the file, then try again.',
  );
  assert.deepEqual(readdirSync(downloads), [], 'nothing set aside');
});
