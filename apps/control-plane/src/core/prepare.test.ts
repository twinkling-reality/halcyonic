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
  const task = await commands.prepare(given(projectScope, 'workstream.create', { title: 'Night' }));
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
  const runtimes = await commands.choices(taskScope, 'runtimes');
  assert.ok(runtimes !== null && runtimes.rows.length > 0);
  const start = await commands.prepare(
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
  const stale = await commands.prepare(
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

  const smuggled = await commands.prepare(
    given(projectScope, 'workstream.create', { title: 'X', project_id: project.project_id }),
  );
  assert.ok(!smuggled.ok, 'an id is never a proposer’s to give');
  // A command this place does not offer is refused before anything is built.
  const elsewhere = await commands.prepare(given(projectScope, 'execution.interrupt', {}));
  assert.ok(!elsewhere.ok);
});

test('a picture is chosen by its place in the folder’s list and given as the reference an instruction takes (A4)', async (t) => {
  const { mkdirSync, mkdtempSync, realpathSync, rmSync, writeFileSync } = await import('node:fs');
  const { tmpdir } = await import('node:os');
  const { join } = await import('node:path');
  const { crc32, deflateSync } = await import('node:zlib');
  const { createHostLocations } = await import('../locations.ts');
  const { createTestControlPlane } = await import('../testing/harness.ts');
  const root = realpathSync(mkdtempSync(join(tmpdir(), 'halcyonic-prepare-pictures-')));
  t.after(() => rmSync(root, { recursive: true, force: true }));
  mkdirSync(join(root, 'game', 'art'), { recursive: true });
  const chunk = (type: string, data: Buffer) => {
    const body = Buffer.concat([Buffer.from(type, 'latin1'), data]);
    const length = Buffer.alloc(4);
    length.writeUInt32BE(data.length);
    const crc = Buffer.alloc(4);
    crc.writeUInt32BE(crc32(body));
    return Buffer.concat([length, body, crc]);
  };
  const header = Buffer.alloc(13);
  header.writeUInt32BE(2, 0);
  header.writeUInt32BE(2, 4);
  header.set([8, 2, 0, 0, 0], 8);
  const row = Buffer.from([0, 200, 40, 40, 200, 40, 40]);
  writeFileSync(
    join(root, 'game', 'art', 'red.png'),
    Buffer.concat([
      Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]),
      chunk('IHDR', header),
      chunk('IDAT', deflateSync(Buffer.concat([row, row]))),
      chunk('IEND', Buffer.alloc(0)),
    ]),
  );
  const { controlPlane, commands } = createTestControlPlane({
    locations: createHostLocations([root]),
  });
  const project = controlPlane.commands.submit(
    commands.createProject('Pictured', { kind: 'existing_folder', root, folder_name: 'game' }),
    'internal',
  );
  const projectId =
    project.command?.result?.kind === 'project_created' ? project.command.result.project_id : '';
  const task = controlPlane.commands.submit(
    commands.createWorkstream(projectId as never, { title: 'Paint', objective: null } as never),
    'internal',
  );
  const workstreamId =
    task.command?.result?.kind === 'workstream_created' ? task.command.result.workstream_id : '';
  const scope = {
    project_id: projectId,
    workstream_id: workstreamId,
    execution_id: null,
  } as HereScope;
  const pictures = await controlPlane.commands.choices(scope, 'pictures');
  assert.ok(pictures !== null);
  assert.deepEqual(
    pictures.rows.map((r) => [r.index, r.kind, r.label]),
    [[0, 'picture', 'art/red.png']],
  );
  const runtimes = await controlPlane.commands.choices(scope, 'runtimes');
  assert.ok(runtimes !== null);
  const prepared = await controlPlane.commands.prepare({
    scope,
    command_type: 'execution.start',
    values: { runtime_id: 0, instruction: 'Paint the sky this colour.', attachments: [0] },
    read: { runtimes: runtimes.digest, pictures: pictures.digest },
    command_id: randomUUID() as CommandId,
    issued_at: '2026-11-12T05:40:00.000Z',
    client: TEST_CLIENT,
  });
  assert.ok(prepared.ok, prepared.ok ? '' : prepared.message);
  assert.deepEqual((prepared.command.payload as { attachments?: unknown }).attachments, [
    { path: 'art/red.png', kind: 'image' },
  ]);
});

test('words that leave the Mac are cleaned the one way: no hidden characters, one line for a title, clipped at a word (addition D)', async () => {
  const { cleanWords } = await import('./prepare.ts');
  assert.equal(
    cleanWords('  Night​ sky\u0007\n  over  the\tcity ', true, 200),
    'Night sky over the city',
  );
  assert.equal(cleanWords('Line one\r\nline two‮', false, 200), 'Line one\nline two');
  assert.equal(cleanWords('Make the whole sky a deep night blue', true, 20), 'Make the whole sky');
  assert.equal(cleanWords('\u0000​', true, 20), '', 'nothing left is no words');
});

test('a project’s own commands are chosen by their places in the list, each with its command’s digest (ADR 0055)', async (t) => {
  const { mkdirSync, mkdtempSync, realpathSync, rmSync, writeFileSync } = await import('node:fs');
  const { tmpdir } = await import('node:os');
  const { join } = await import('node:path');
  const { createHostLocations } = await import('../locations.ts');
  const { createTestControlPlane } = await import('../testing/harness.ts');
  const { managersOn, commandSha256 } = await import('../own-check/own-check.ts');
  const root = realpathSync(mkdtempSync(join(tmpdir(), 'halcyonic-prepare-scripts-')));
  t.after(() => rmSync(root, { recursive: true, force: true }));
  mkdirSync(join(root, 'site'));
  writeFileSync(
    join(root, 'site', 'package.json'),
    JSON.stringify({ name: 'site', scripts: { test: 'node --test', lint: 'echo lint' } }),
  );
  const { controlPlane, commands } = createTestControlPlane({
    locations: createHostLocations([root]),
    ownChecks: {
      dataDir: join(root, 'data'),
      home: '/Users/someone',
      node: process.execPath,
      managers: managersOn(process.env.PATH),
    },
  });
  t.after(() => controlPlane.ownChecks?.close());
  const created = controlPlane.commands.submit(
    commands.createProject('Site', { kind: 'existing_folder', root, folder_name: 'site' }),
    'internal',
  );
  const projectId =
    created.command?.result?.kind === 'project_created' ? created.command.result.project_id : '';
  const scope = { project_id: projectId, workstream_id: null, execution_id: null } as HereScope;
  const scripts = await controlPlane.commands.choices(scope, 'own_check_scripts');
  assert.ok(scripts !== null);
  const names = scripts.rows.map((r) => r.label);
  assert.ok(names.includes('test') && names.includes('lint'), names.join(', '));
  const prepared = await controlPlane.commands.prepare({
    scope,
    command_type: 'project.own_check.choose',
    values: { scripts: [names.indexOf('test')] },
    read: { own_check_scripts: scripts.digest },
    command_id: randomUUID() as CommandId,
    issued_at: '2026-11-12T05:45:00.000Z',
    client: TEST_CLIENT,
  });
  assert.ok(prepared.ok, prepared.ok ? '' : prepared.message);
  assert.deepEqual((prepared.command.payload as { scripts?: unknown }).scripts, [
    { name: 'test', command_sha256: commandSha256('node --test') },
  ]);
});
