import assert from 'node:assert/strict';
import { randomUUID } from 'node:crypto';
import { mkdirSync, mkdtempSync, realpathSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { after, test } from 'node:test';
import type { CommandId, ProjectId } from '@halcyonic/contracts';
import { createHostLocations } from '../locations.ts';
import { createTestControlPlane, TEST_CLIENT } from '../testing/harness.ts';
import { readRules, rulesReview } from './project-rules.ts';
import { locationReview } from './review.ts';
import { buildReview } from './reviews.ts';

const root = realpathSync(mkdtempSync(join(tmpdir(), 'halcyonic-reviews-')));
after(() => rmSync(root, { recursive: true, force: true }));

test('the one review: what the list offers, prepared from values, reviewed with the button’s own digest (ADR 0060)', async () => {
  mkdirSync(join(root, 'app'));
  mkdirSync(join(root, 'other'));
  writeFileSync(join(root, 'app', 'AGENTS.md'), 'Use tabs.\n');
  const { controlPlane, commands } = createTestControlPlane({
    locations: createHostLocations([root]),
  });
  const created = controlPlane.commands.submit(
    commands.createProject('Reviewed', { kind: 'existing_folder', root, folder_name: 'app' }),
    'internal',
  );
  const projectId = (
    created.command?.result?.kind === 'project_created' ? created.command.result.project_id : ''
  ) as ProjectId;
  const scope = { project_id: projectId, workstream_id: null, execution_id: null };
  const given = (command_type: string, values: Record<string, unknown>) => ({
    scope,
    command_type,
    values: values as Record<string, boolean>,
    read: {},
    command_id: randomUUID() as CommandId,
    issued_at: '2026-11-12T05:40:00.000Z',
    client: TEST_CLIENT,
  });

  // Project Rules: offered with a readable rules file, its review the button's own digest.
  const rules = (await controlPlane.commands.here(scope)).find(
    (action) => action.kind === 'command' && action.command_type === 'project.rules.set',
  );
  assert.ok(rules?.kind === 'command' && rules.confirm === 'review');
  const prepared = await controlPlane.commands.prepare(given('project.rules.set', { on: true }));
  assert.ok(prepared.ok, prepared.ok ? '' : prepared.message);
  const built = await buildReview(controlPlane, prepared.command);
  assert.ok(built.ok, built.ok ? '' : built.message);
  const read = readRules(join(root, 'app'));
  assert.ok(read.state === 'ok');
  assert.equal(built.review.digest, rulesReview(projectId, read.file, read.sha256));
  assert.equal(built.review.confirm_words, 'Use Rules');
  assert.deepEqual(built.review.said, [], 'a yes or no is not words that go out');
  assert.deepEqual(built.review.facts, [{ kind: 'words', value: 'AGENTS.md', theirs: false }]);
  assert.equal(
    built.review.items[0]?.theirs,
    true,
    'the file’s words are the project’s, said as theirs',
  );

  // Carried back, the digest is the one the command's own admission checks: it is admitted.
  const sent = controlPlane.commands.submit(
    {
      ...prepared.command,
      payload: { ...prepared.command.payload, review: built.review.digest },
    } as typeof prepared.command,
    'internal',
  );
  assert.equal(sent.disposition, 'accepted');

  // A move's review names both folders and digests as the location review does.
  const move = {
    ...commands.setLocation(projectId, { kind: 'existing_folder', root, folder_name: 'other' }),
  };
  const moved = await buildReview(controlPlane, move);
  assert.ok(moved.ok);
  const project = controlPlane.projection.project(projectId);
  assert.equal(
    moved.review.digest,
    locationReview(projectId, move.payload.location, project?.location ?? null),
  );
  assert.equal(moved.review.facts.length, 2);

  // A command confirmed by its one press has no review.
  const none = await buildReview(
    controlPlane,
    commands.createWorkstream(projectId, {
      title: 'Night',
      objective: null,
    } as never),
  );
  assert.ok(!none.ok);
});

test('a review says whole the words given that go out with the command, by their labels (SCREENS, addition D)', async () => {
  const { controlPlane, commands } = createTestControlPlane({});
  const command = {
    ...commands.createProject('Words', null),
    command_type: 'project.send_to_github',
    payload: {
      project_id: '01920000-0000-7000-8000-00000000e001',
      title: 'Make the sky night',
      review: '0'.repeat(64),
    },
  } as never;
  // Send has no folder here, so its review is refused, yet what it would say is computed the one way.
  const built = await buildReview(controlPlane, command);
  assert.ok(!built.ok);
  const { saidIn } = await import('./reviews.ts');
  assert.deepEqual(saidIn(command), [{ label: 'Title', words: 'Make the sky night' }]);
});
