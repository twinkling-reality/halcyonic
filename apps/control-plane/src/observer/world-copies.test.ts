import assert from 'node:assert/strict';
import { mkdirSync, mkdtempSync, readFileSync, realpathSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { after, test } from 'node:test';
import { fileURLToPath } from 'node:url';
import {
  type CommandEnvelope,
  type CommandId,
  type EventEnvelope,
  type ProjectId,
  type RuntimeDescriptor,
  type RuntimeId,
  WORLD_FILE,
  WORLD_STARTER_ID,
  type WorkstreamId,
} from '@halcyonic/contracts';
import type { ObservationSink, RuntimeAdapter, RuntimeObservation } from '@halcyonic/runtime-core';
import { createVirtualTime } from '@halcyonic/runtime-core';
import { CheckpointStore } from '../checkpoints/store.ts';
import { ControlPlane } from '../core/control-plane.ts';
import { createUuidV7Generator } from '../ids.ts';
import { openSqliteJournal } from '../journal/sqlite-journal.ts';
import { createHostLocations } from '../locations.ts';
import { HostStarters } from '../starters.ts';
import { capturingLogger } from '../testing/harness.ts';

const base = realpathSync(mkdtempSync(join(tmpdir(), 'halcyonic-world-copies-')));
after(() => rmSync(base, { recursive: true, force: true }));
const TRACKED = fileURLToPath(new URL('../../../../starters', import.meta.url));
const starting = JSON.parse(
  readFileSync(join(TRACKED, WORLD_STARTER_ID, 'template', WORLD_FILE), 'utf8'),
);
const night = { ...starting, night: 0.9 };

async function until(check: () => boolean, what: string) {
  for (let i = 0; i < 1000; i += 1) {
    if (check()) return;
    await new Promise((r) => setTimeout(r, 10));
  }
  assert.fail(`timed out waiting for ${what}`);
}

// AUDIT2's probe of copies on by default (TASKS-copies-default-VERDICT.md), kept as a test: a world's
// task works in the project's own folder, so the world the headset shows follows each turn (ADR 0045).
test("a world project's task gets no copy: it changes the world in place, and the world shown follows its turn", {
  skip: process.platform !== 'darwin',
}, async () => {
  const root = join(base, 'w');
  const projects = join(root, 'projects');
  mkdirSync(projects, { recursive: true });
  let n = 0;
  const sinks: ObservationSink[] = [];
  const descriptor: RuntimeDescriptor = {
    runtime_id: 'writer' as RuntimeId,
    kind: 'stub',
    display_name: 'writer',
    synthetic: true,
    capabilities: {
      start_execution: true,
      instruct_at_rest: false,
      instruct_while_running: false,
      respond_to_approval: false,
      answer_question: false,
      interrupt: false,
    },
    model_choice: 'none',
    uses_project_location: true,
    reports_tool_activity: false,
  };
  const observe = (sink: ObservationSink, type: RuntimeObservation['type'], payload: unknown) => {
    n += 1;
    sink({
      type,
      occurred_at: new Date().toISOString(),
      native_event_id: `w-${n}`,
      sequence: null,
      provenance: { epistemic: 'observed', native_type: null },
      payload,
    } as RuntimeObservation);
  };
  const adapter: RuntimeAdapter = {
    descriptor,
    validateStartOptions: () => ({ ok: true }),
    startExecution: async (request) => {
      sinks.push(request.emit);
      observe(request.emit, 'runtime.execution.started', { native_id: 's' });
      observe(request.emit, 'runtime.turn.started', { turn_id: 'turn-1' });
      writeFileSync(join(request.directory ?? '', WORLD_FILE), JSON.stringify(night));
      return { native_id: 's' };
    },
    close: async () => {},
  };
  const time = createVirtualTime(new Date('2026-11-10T10:00:00.000Z'));
  const ids = createUuidV7Generator({ now: () => Date.now() });
  const journal = openSqliteJournal({ path: ':memory:', originIfNew: 'live', ids });
  const controlPlane = new ControlPlane({
    journal,
    adapters: [adapter],
    ids,
    clock: time,
    scheduler: time,
    logger: capturingLogger().logger,
    commandTimeoutMs: 30_000,
    locations: createHostLocations([projects]),
    observeFiles: { protect: [] },
    starters: new HostStarters({
      trackedDir: TRACKED,
      installedDir: join(root, 'installed'),
      dataDir: join(root, 'data'),
      home: join(root, 'home'),
    }),
    checkpoints: {
      store: new CheckpointStore({ git: '/usr/bin/git', directory: join(root, 'checkpoints') }),
      copies: { directory: join(root, 'tasks'), heldToFolder: (id: string) => id === 'writer' },
    },
  } as never);
  let c = 0;
  const submit = (type: string, payload: unknown) => {
    c += 1;
    return controlPlane.commands.submit(
      {
        schema_version: 1,
        command_id: `00000000-0000-4000-8000-${String(c).padStart(12, '0')}` as CommandId,
        issued_at: '2026-11-10T10:00:00.000Z',
        client: { name: 'test', version: null, device_label: null },
        command_type: type,
        payload,
      } as CommandEnvelope,
      'internal',
    );
  };
  const events = (): EventEnvelope[] => [...journal.readAll()].map((s) => s.event);
  const worlds = () => events().filter((e) => e.event_type === 'project.world.read').length;
  const made = submit('project.create', {
    name: 'My World',
    location: {
      kind: 'new_from_starter',
      root: projects,
      folder_name: 'world',
      starter_id: WORLD_STARTER_ID,
    },
  });
  const result = made.command?.result;
  const projectId = (result?.kind === 'project_created' ? result.project_id : '') as ProjectId;
  await until(() => worlds() === 1, 'the starting world');
  const work = submit('workstream.create', {
    project_id: projectId,
    title: 'Evening',
    objective: null,
  }).command?.result as { workstream_id: WorkstreamId };
  submit('execution.start', {
    workstream_id: work.workstream_id,
    runtime_id: 'writer',
    instruction: 'Make it night.',
    options: {},
    model_ref: null,
  });
  await until(() => events().some((e) => e.event_type === 'runtime.turn.started'), 'the turn');
  for (const sink of sinks.splice(0))
    observe(sink, 'runtime.turn.completed', { turn_id: 'turn-1' });
  await until(
    () => events().some((e) => e.event_type === 'execution.files.observed'),
    'files observed',
  );
  await new Promise((r) => setTimeout(r, 300));
  const copy = controlPlane.projection.workstream(work.workstream_id)?.copy;
  assert.equal(copy, undefined, 'no copy, and none recorded as refused');
  const shown = () =>
    (
      controlPlane.projection.project(projectId)?.world?.description as
        | { night?: number }
        | undefined
    )?.night;
  await until(() => worlds() === 2, 'the world read again after the turn');
  assert.equal(shown(), night.night, 'the world shown is the one the turn wrote');
  assert.equal(
    JSON.parse(readFileSync(join(projects, 'world', WORLD_FILE), 'utf8')).night,
    night.night,
  );
  await controlPlane.close();
});
