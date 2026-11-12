import assert from 'node:assert/strict';
import { randomUUID } from 'node:crypto';
import { test } from 'node:test';
import type { CommandId, HereScope } from '@halcyonic/contracts';
import { startTestServer, TEST_CLIENT } from '../testing/harness.ts';

test('one preparation builds a command from values and indices, never from a proposer’s ids (ADR 0060)', async (t) => {
  const server = await startTestServer();
  t.after(() => server.stop());
  const commands = server.controlPlane.commands;
  const post = async (body: unknown) =>
    fetch(`${server.baseUrl}/api/commands`, {
      method: 'POST',
      headers: { authorization: `Bearer ${server.token}`, 'content-type': 'application/json' },
      body: JSON.stringify(body),
    });
  const until = async (found: () => boolean) => {
    for (let i = 0; i < 100 && !found(); i += 1) await new Promise((r) => setTimeout(r, 20));
    assert.ok(found());
  };
  const given = (
    scope: HereScope,
    command_type: string,
    values: Record<string, unknown>,
    read = {},
  ) => ({
    scope,
    command_type,
    values: values as Record<string, string>,
    read,
    command_id: randomUUID() as CommandId,
    issued_at: '2026-11-12T05:30:00.000Z',
    client: TEST_CLIENT,
  });

  assert.equal((await post(server.commands.createProject('Prepared'))).status, 202);
  const project = server.controlPlane.snapshot().projects.find((p) => p.name === 'Prepared');
  assert.ok(project);
  const projectScope = { project_id: project.project_id, workstream_id: null, execution_id: null };

  // The person's press for New Task: words only; the project's id is the scope's.
  const task = commands.prepare(given(projectScope, 'workstream.create', { title: 'Night' }));
  assert.ok(task.ok, task.ok ? '' : task.message);
  assert.deepEqual(task.command.payload, {
    project_id: project.project_id,
    title: 'Night',
    objective: null,
  });
  assert.equal((await post(task.command)).status, 202);
  await until(() => server.controlPlane.projection.workstreams(project.project_id).length === 1);
  const workstream = server.controlPlane.projection.workstreams(project.project_id)[0];
  assert.ok(workstream);
  const taskScope = { ...projectScope, workstream_id: workstream.workstream_id };

  // A runtime by its place in the host's list, read with its digest.
  const runtimes = commands.choices(taskScope, 'runtimes');
  assert.ok(runtimes !== null && runtimes.rows.length > 0);
  const start = commands.prepare(
    given(
      taskScope,
      'execution.start',
      { runtime_id: 0, instruction: 'Make it night.' },
      {
        runtimes: runtimes.digest,
      },
    ),
  );
  assert.ok(start.ok, start.ok ? '' : start.message);
  const payload = start.command.payload as Record<string, unknown>;
  assert.equal(payload.workstream_id, workstream.workstream_id);
  assert.equal(payload.runtime_id, 'mock', 'the index became the host’s own id');
  assert.deepEqual(payload.options, {}, 'the host fills the runtime options');
  assert.equal(payload.model_ref, null, 'a model not chosen is given as none');
  // A list read before it changed is refused.
  const stale = commands.prepare(
    given(
      taskScope,
      'execution.start',
      { runtime_id: 0, instruction: 'Again.' },
      {
        runtimes: '0'.repeat(64),
      },
    ),
  );
  assert.ok(!stale.ok && stale.code === 'choices_changed');
  // Submitted, it meets the whole admission like any other command, and is accepted.
  const sent = await post(start.command);
  assert.equal(sent.status, 202);
  const outcome = (await sent.json()) as { disposition: string };
  assert.equal(outcome.disposition, 'accepted');
  // An id offered for a field the scope fills is refused.

  const smuggled = commands.prepare(
    given(projectScope, 'workstream.create', { title: 'X', project_id: project.project_id }),
  );
  assert.ok(!smuggled.ok, 'an id is never a proposer’s to give');
  // A command this place does not offer is refused before anything is built.
  const elsewhere = commands.prepare(given(projectScope, 'execution.interrupt', {}));
  assert.ok(!elsewhere.ok);
});
