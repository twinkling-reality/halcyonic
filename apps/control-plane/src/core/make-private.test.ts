import assert from 'node:assert/strict';
import { randomUUID } from 'node:crypto';
import { mkdtempSync, readFileSync, realpathSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { after, test } from 'node:test';
import type { CommandEnvelope, CommandId, ProjectId } from '@halcyonic/contracts';
import { createHostLocations } from '../locations.ts';
import { standInGitHub } from '../testing/github-stand-in.ts';
import { createTestControlPlane, TEST_CLIENT } from '../testing/harness.ts';
import { controlPlaneDraft, NO_CAUSE } from './drafts.ts';

// The way back after a publish made the repository public and then failed (run 2, 2026-11-12): Make
// Private, offered where the person stands only for the repository Halcyonic made and made public, done
// with the person's own gh (a stand-in here), recorded once GitHub did it. Nothing reaches github.com.

const base = realpathSync(mkdtempSync(join(tmpdir(), 'halcyonic-make-private-')));
after(() => rmSync(base, { recursive: true, force: true }));

async function until(check: () => boolean, what: string): Promise<void> {
  const end = Date.now() + 15_000;
  while (!check()) {
    if (Date.now() > end) throw new Error(`timed out waiting for ${what}`);
    await new Promise((resolve) => setTimeout(resolve, 20));
  }
}

test("Make Private is offered after Make Public, puts the repository back with the person's gh, and is recorded (run 2)", {
  skip: process.platform !== 'darwin',
}, async () => {
  const stand = standInGitHub(base);
  const { controlPlane, commands } = createTestControlPlane({
    locations: createHostLocations([stand.projects]),
    github: {
      git: '/usr/bin/git',
      gh: stand.ghPath,
      dataDir: join(base, 'data'),
      env: { HOME: stand.home },
    },
  });
  const created = controlPlane.commands.submit(
    commands.createProject('Garden', {
      kind: 'existing_folder',
      root: stand.projects,
      folder_name: 'garden',
    }),
    'internal',
  );
  const projectId = (
    created.command?.result?.kind === 'project_created' ? created.command.result.project_id : ''
  ) as ProjectId;
  const scope = { project_id: projectId, workstream_id: null, execution_id: null };
  const at = '2026-11-12T14:40:00.000Z' as never;
  const offered = async () =>
    (await controlPlane.commands.here(scope)).some(
      (action) => action.kind === 'command' && action.command_type === 'project.make_private',
    );
  // Made by the first Send, as gh spells it, still private: nothing to make private.
  controlPlane.recorder.record(
    controlPlaneDraft(
      'project.repository_made',
      scope,
      { repository: 'Owner/garden', visibility: 'private' },
      at,
      NO_CAUSE,
    ),
  );
  assert.equal(await offered(), false);
  // Made public by a publish whose push GitHub then refused: the way back is offered.
  controlPlane.recorder.record(
    controlPlaneDraft(
      'project.repository_made_public',
      scope,
      { repository: 'owner/garden' },
      at,
      NO_CAUSE,
    ),
  );
  assert.equal(await offered(), true);
  const command = {
    schema_version: 1,
    command_id: randomUUID() as CommandId,
    issued_at: at,
    client: TEST_CLIENT,
    command_type: 'project.make_private',
    payload: { project_id: projectId },
  } as CommandEnvelope;
  assert.equal(controlPlane.commands.submit(command, 'internal').disposition, 'accepted');
  await until(() => {
    const status = controlPlane.projection.command(command.command_id)?.status;
    return status === 'completed' || status === 'failed';
  }, 'Make Private');
  assert.equal(controlPlane.projection.command(command.command_id)?.status, 'completed');
  assert.match(
    readFileSync(stand.ghLog, 'utf8'),
    /repo edit Owner\/garden --visibility private --accept-visibility-change-consequences/,
    'the repository the journal names, as gh made it',
  );
  assert.deepEqual(controlPlane.projection.madeRepository(projectId), {
    repository: 'Owner/garden',
    visibility: 'private',
  });
  assert.equal(await offered(), false, 'private again: nothing left to make private');
});
