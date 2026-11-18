import assert from 'node:assert/strict';
import { existsSync, mkdirSync, mkdtempSync, realpathSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { after, test } from 'node:test';
import type { CommandId, ExecutionId, ProjectId, WorkstreamId } from '@halcyonic/contracts';
import { MockRuntimeAdapter } from '@halcyonic/integration-mock';
import type { StartExecutionRequest } from '@halcyonic/runtime-core';
import { createHostLocations } from '../locations.ts';
import { createTestControlPlane, SCENARIOS, startTestServer } from '../testing/harness.ts';
import { clip, StandInTool } from '../testing/media-stand-in.ts';
import { MEDIA_IS_REFERENCE } from './command-service.ts';

const root = realpathSync(mkdtempSync(join(tmpdir(), 'halcyonic-media-command-')));
after(() => rmSync(root, { recursive: true, force: true }));

// A poll stops when its condition holds; its bound is long, so a loaded machine is slow, never a failure (CORE, 15:14Z).
const POLL_TRIES = 6000;

async function settle(time: { runUntilIdle(): Promise<void> }, done: () => boolean) {
  for (let i = 0; i < POLL_TRIES && !done(); i += 1) {
    await time.runUntilIdle();
    await new Promise((resolve) => setTimeout(resolve, 5));
  }
  await time.runUntilIdle();
}

async function* chunks(bytes: Buffer): AsyncIterable<Uint8Array> {
  yield bytes;
}

test('Add Media places a video, records it added then prepared, completes only after, and a task given it gets its stills and the host line (ADR 0066)', async (t) => {
  const projects = join(root, 'projects');
  mkdirSync(join(projects, 'camel'), { recursive: true });
  const data = join(root, 'data');
  const starts: StartExecutionRequest[] = [];
  const { controlPlane, commands, time } = createTestControlPlane({
    locations: createHostLocations([projects]),
    media: { dataDir: data, tool: new StandInTool(join(data, 'media-work')) },
    adapters: (clock) => {
      const mock = new MockRuntimeAdapter({
        scenarios: SCENARIOS,
        clock,
        scheduler: clock,
        takesPictures: true,
      });
      const start = mock.startExecution.bind(mock);
      mock.startExecution = (request: StartExecutionRequest) => {
        starts.push(request);
        return start(request);
      };
      return [mock];
    },
  });
  t.after(() => controlPlane.close());
  const created = controlPlane.commands.submit(
    commands.createProject('Camel', {
      kind: 'existing_folder',
      root: projects,
      folder_name: 'camel',
    }),
    'internal',
  );
  const projectId = (
    created.command?.result?.kind === 'project_created' ? created.command.result.project_id : ''
  ) as ProjectId;
  const store = controlPlane.mediaStore;
  assert.ok(store);
  const received = await store.receive(chunks(clip()), clip().length);
  assert.ok(received.ok);

  const add = {
    schema_version: 1,
    command_id: '01920000-0000-7000-8000-00000000c001' as CommandId,
    issued_at: time.now().toISOString(),
    client: { name: 'test', version: null, device_label: null },
    command_type: 'project.media.add',
    payload: { project_id: projectId, media: received.id },
  } as never;
  const ack = controlPlane.commands.submit(add, 'http', { kind: 'local' });
  assert.equal(ack.disposition, 'accepted');
  await settle(
    time,
    () =>
      controlPlane.projection.command('01920000-0000-7000-8000-00000000c001')?.status ===
      'completed',
  );
  const types = [...controlPlane.journal.readAll()].map((stored) => stored.event.event_type);
  const added = types.indexOf('project.media.added');
  const prepared = types.indexOf('project.media.prepared');
  const completed = types.lastIndexOf('command.completed');
  assert.ok(added > 0 && prepared > added && completed > prepared, types.join(' '));
  const [medium] = controlPlane.projection.media(projectId);
  assert.ok(medium);
  assert.match(medium.path, /^media\/video-[0-9a-f]{12}\.mov$/);
  assert.equal(medium.prepared, true);
  assert.equal(medium.duration_ms, 6_565);
  assert.ok(existsSync(join(projects, 'camel', medium.path)));
  // The upload's digest is the command's own payload, and in nothing the host records of the file.
  const recorded = [...controlPlane.journal.readAll()]
    .map((stored) => stored.event)
    .filter((event) => event.event_type.startsWith('project.media.'));
  assert.equal(recorded.length, 2);
  assert.equal(JSON.stringify(recorded).includes(received.id), false);

  // A task given the video: its four stills as pictures, the host's line after the person's words.
  const task = controlPlane.commands.submit(
    commands.createWorkstream(projectId, {
      title: 'Dunes',
      objective: 'A place to stand in.',
      instruction: '',
      scenario: '',
    } as never),
    'internal',
  );
  const workstreamId = (
    task.command?.result?.kind === 'workstream_created' ? task.command.result.workstream_id : ''
  ) as WorkstreamId;
  const start = commands.startExecution(workstreamId, {
    title: 'Dunes',
    objective: 'A place to stand in.',
    instruction: 'Make a place I can stand in from this video.',
    scenario: MockRuntimeAdapter.DEFAULT_SCENARIO,
  } as never);
  controlPlane.commands.submit(
    {
      ...start,
      payload: { ...start.payload, attachments: [{ kind: 'media', path: medium.path }] },
    } as typeof start,
    'internal',
  );
  await settle(time, () => starts.length > 0);
  assert.equal(starts[0]?.pictures?.length, 4);
  const instruction = String(starts[0]?.instruction);
  assert.ok(instruction.startsWith('Make a place I can stand in from this video.'));
  assert.match(
    instruction,
    new RegExp(`From Halora: the person gave this task ${medium.path}, a 7-second video`),
  );
  // The person's media is what to build, never a picture to hang in its place unless they ask (the owner, 2026-11-18).
  assert.ok(
    instruction.endsWith(`\n${MEDIA_IS_REFERENCE}`),
    "the reference line closes the host's lines",
  );
  assert.equal(instruction.split(MEDIA_IS_REFERENCE).length, 2, 'once, however many media');
  const events = [...controlPlane.journal.readAll()].map((stored) => stored.event);
  const given = events.find((event) => event.event_type === 'execution.instruction.media_given');
  assert.deepEqual(given?.payload, {
    command_id: start.command_id,
    media: [{ path: medium.path, media_type: 'video/quicktime', stills_given: 4 }],
    line_added: true,
  });
  assert.equal(
    JSON.stringify(events).includes('From Halora: the person gave'),
    false,
    'the line is never journaled',
  );
  const executionId = given?.execution_id as ExecutionId;
  assert.deepEqual(controlPlane.projection.execution(executionId)?.media?.[0]?.path, medium.path);
});

test('the headset reads a still by the picture route, and a medium whose file left the folder answers media_gone (ADR 0066)', async (t) => {
  const projects = join(root, 'route-projects');
  mkdirSync(join(projects, 'camel'), { recursive: true });
  const data = join(root, 'route-data');
  const server = await startTestServer({
    locations: createHostLocations([projects]),
    media: { dataDir: data, tool: new StandInTool(join(data, 'media-work')) },
  });
  t.after(() => server.stop());
  const { controlPlane, commands } = server;
  const created = controlPlane.commands.submit(
    commands.createProject('Camel', {
      kind: 'existing_folder',
      root: projects,
      folder_name: 'camel',
    }),
    'internal',
  );
  const projectId = (
    created.command?.result?.kind === 'project_created' ? created.command.result.project_id : ''
  ) as ProjectId;
  const store = controlPlane.mediaStore;
  assert.ok(store);
  const received = await store.receive(chunks(clip()), clip().length);
  assert.ok(received.ok);
  controlPlane.commands.submit(
    {
      schema_version: 1,
      command_id: '01920000-0000-7000-8000-00000000c002',
      issued_at: new Date().toISOString(),
      client: { name: 'test', version: null, device_label: null },
      command_type: 'project.media.add',
      payload: { project_id: projectId, media: received.id },
    } as never,
    'http',
    { kind: 'local' },
  );
  for (
    let i = 0;
    i < POLL_TRIES &&
    controlPlane.projection.command('01920000-0000-7000-8000-00000000c002')?.status !== 'completed';
    i += 1
  )
    await new Promise((resolve) => setTimeout(resolve, 5));
  const [medium] = controlPlane.projection.media(projectId);
  assert.ok(medium?.still);
  const journal = server.journal.info.journal_id;
  const get = (path: string) =>
    fetch(
      `${server.baseUrl}/api/projects/${projectId}/picture?journal_id=${journal}&path=${encodeURIComponent(path)}`,
      { headers: { authorization: `Bearer ${server.token}` } },
    );
  const still = await get(medium.still);
  assert.equal(still.status, 200);
  assert.equal(still.headers.get('content-type'), 'image/png');
  rmSync(join(projects, 'camel', medium.still));
  const gone = await get(medium.still);
  assert.equal(gone.status, 404);
  assert.equal(
    ((await gone.json()) as { code?: string; error?: { code?: string } }).error?.code ?? '',
    'media_gone',
  );
  assert.equal((await get('src/none.png')).status, 404);
});
