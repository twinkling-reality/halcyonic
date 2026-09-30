import assert from 'node:assert/strict';
import {
  chmodSync,
  existsSync,
  mkdirSync,
  mkdtempSync,
  realpathSync,
  rmSync,
  symlinkSync,
} from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { after, describe, test } from 'node:test';
import {
  compileValidator,
  LocationsResponse,
  type ProjectId,
  type ProjectLocationChoice,
  type RuntimeDescriptor,
  type RuntimeId,
  type WorkstreamId,
} from '@halcyonic/contracts';
import { MockRuntimeAdapter } from '@halcyonic/integration-mock';
import {
  confirmProjectLocation,
  type RuntimeAdapter,
  type StartExecutionRequest,
} from '@halcyonic/runtime-core';
import { DEMO_WORKSTREAMS } from '../demo-plan.ts';
import { createHostLocations, type HostLocations } from '../locations.ts';
import { createTestControlPlane, SCENARIOS, startTestServer } from '../testing/harness.ts';

const FEATURE = DEMO_WORKSTREAMS[0] as (typeof DEMO_WORKSTREAMS)[number];
const base = realpathSync(mkdtempSync(join(tmpdir(), 'halcyonic-project-location-')));
after(() => rmSync(base, { recursive: true, force: true }));

/**
 * A runtime that works in the project's folder, as the real ones do, asking the host's policy
 * again before it "starts", and keeping every request it received.
 */
function folderRuntime(locations: HostLocations) {
  const requests: StartExecutionRequest[] = [];
  const descriptor: RuntimeDescriptor = {
    runtime_id: 'folder' as RuntimeId,
    kind: 'stub',
    display_name: 'Stub that works in a folder',
    synthetic: true,
    capabilities: {
      start_execution: true,
      instruct_at_rest: false,
      instruct_while_running: false,
      respond_to_approval: false,
      interrupt: false,
    },
    model_choice: 'none',
    uses_project_location: true,
  };
  const adapter: RuntimeAdapter = {
    descriptor,
    validateStartOptions: () => ({ ok: true }),
    startExecution: async (request) => {
      requests.push(request);
      confirmProjectLocation(locations.policy, request.directory);
      return { native_id: `folder-session-${requests.length}` };
    },
    close: async () => {},
  };
  return { adapter, requests };
}

function setup(name: string) {
  const root = join(base, name);
  mkdirSync(join(root, 'app'), { recursive: true });
  const locations = createHostLocations([root]);
  const runtime = folderRuntime(locations);
  const harness = createTestControlPlane({
    locations,
    adapters: (time) => [
      new MockRuntimeAdapter({ scenarios: SCENARIOS, clock: time, scheduler: time }),
      runtime.adapter,
    ],
  });
  const { controlPlane, commands } = harness;
  const submit = (command: Parameters<typeof controlPlane.commands.submit>[0]) =>
    controlPlane.commands.submit(command, 'internal');
  const createProject = (location: ProjectLocationChoice | null) => {
    const outcome = submit(commands.createProject('Storefront', location));
    const result = outcome.command?.result;
    return { outcome, projectId: result?.kind === 'project_created' ? result.project_id : null };
  };
  const createWorkstream = (projectId: ProjectId): WorkstreamId => {
    const created = submit(commands.createWorkstream(projectId, FEATURE));
    if (created.command?.result?.kind !== 'workstream_created') throw new Error('no workstream');
    return created.command.result.workstream_id;
  };
  const start = (workstreamId: WorkstreamId) =>
    submit({
      ...commands.startExecution(workstreamId, FEATURE, 'folder' as RuntimeId),
      payload: {
        workstream_id: workstreamId,
        runtime_id: 'folder' as RuntimeId,
        instruction: FEATURE.instruction,
        options: {},
        model_ref: null,
      },
    });
  return { ...harness, root, locations, runtime, submit, createProject, createWorkstream, start };
}

describe('creating a project where its files will live', () => {
  test('in a new folder: the host makes it, and the project records where', async () => {
    const { controlPlane, root, createProject, journal } = setup('new-folder');
    const { outcome, projectId } = createProject({
      kind: 'new_folder',
      root,
      folder_name: 'storefront',
    });
    assert.equal(outcome.disposition, 'accepted');
    assert.equal(outcome.command?.status, 'completed');
    const project = controlPlane.projection.project(projectId ?? '');
    assert.deepEqual(project?.location, {
      path: join(root, 'storefront'),
      name: 'storefront',
      created: true,
    });
    assert.ok(existsSync(join(root, 'storefront')));
    assert.deepEqual(
      [...journal.readAll()].map((stored) => stored.event.event_type),
      ['command.accepted', 'project.created', 'command.completed'],
    );
    await controlPlane.close();
  });

  test('refusals carry the location code, are journaled, and make nothing', async () => {
    const { controlPlane, root, createProject } = setup('refusals');
    const outside = join(base, 'refusals-outside');
    mkdirSync(outside);
    const refusals: [ProjectLocationChoice, string][] = [
      [{ kind: 'new_folder', root, folder_name: 'app' }, 'location_exists'],
      [{ kind: 'existing_folder', root, folder_name: 'missing' }, 'location_missing'],
      [{ kind: 'existing_folder', root: outside, folder_name: null }, 'location_not_allowed'],
      [{ kind: 'new_folder', root: outside, folder_name: 'sneaky' }, 'location_not_allowed'],
    ];
    for (const [choice, code] of refusals) {
      const { outcome } = createProject(choice);
      assert.equal(outcome.disposition, 'rejected', JSON.stringify(choice));
      assert.equal(outcome.command?.rejection?.code, code, JSON.stringify(choice));
    }
    assert.deepEqual(controlPlane.projection.projects(), []);
    assert.equal(existsSync(join(outside, 'sneaky')), false);
    await controlPlane.close();
  });

  test('a folder the file system will not make fails the command with no effect and no project', async (t) => {
    const { controlPlane, root, createProject } = setup('read-only');
    chmodSync(root, 0o555);
    t.after(() => chmodSync(root, 0o755));
    const { outcome } = createProject({ kind: 'new_folder', root, folder_name: 'storefront' });
    assert.equal(outcome.disposition, 'accepted');
    assert.equal(outcome.command?.status, 'failed');
    assert.deepEqual(
      outcome.command?.failure && {
        code: outcome.command.failure.code,
        effect: outcome.command.failure.effect,
      },
      { code: 'location_not_created', effect: 'none' },
    );
    assert.deepEqual(controlPlane.projection.projects(), []);
    await controlPlane.close();
  });

  test('a folder made but not usable fails the command with an unknown effect, not none', async () => {
    const root = join(base, 'made-unusable');
    mkdirSync(root);
    const locations = createHostLocations([root], () => ({
      ok: false,
      code: 'location_not_allowed',
      message: 'Not allowed now.',
    }));
    const { controlPlane, commands } = createTestControlPlane({ locations });
    const outcome = controlPlane.commands.submit(
      commands.createProject('Storefront', { kind: 'new_folder', root, folder_name: 'storefront' }),
      'internal',
    );
    assert.equal(outcome.command?.status, 'failed');
    assert.equal(outcome.command?.failure?.code, 'location_not_created');
    assert.equal(outcome.command?.failure?.effect, 'unknown');
    assert.deepEqual(controlPlane.projection.projects(), []);
    await controlPlane.close();
  });

  test('after a crash left a new folder without its project, the person chooses it as existing', async () => {
    const { controlPlane, root, createProject } = setup('crash');
    // What a control plane that died after making the folder leaves behind.
    mkdirSync(join(root, 'storefront'));
    const retried = createProject({ kind: 'new_folder', root, folder_name: 'storefront' });
    assert.equal(retried.outcome.command?.rejection?.code, 'location_exists');
    const chosen = createProject({ kind: 'existing_folder', root, folder_name: 'storefront' });
    assert.equal(chosen.outcome.command?.status, 'completed');
    assert.deepEqual(controlPlane.projection.project(chosen.projectId ?? '')?.location, {
      path: join(root, 'storefront'),
      name: 'storefront',
      created: false,
    });
    await controlPlane.close();
  });
});

describe('starting work in the project folder', () => {
  test('the runtime is given the folder, and the execution records it', async () => {
    const { controlPlane, time, root, runtime, createProject, createWorkstream, start } =
      setup('start');
    const { projectId } = createProject({ kind: 'existing_folder', root, folder_name: 'app' });
    const workstreamId = createWorkstream(projectId as ProjectId);
    const started = start(workstreamId);
    assert.equal(started.disposition, 'accepted');
    await time.runUntilIdle();
    assert.equal(
      controlPlane.projection.command(started.command?.command_id ?? '')?.status,
      'completed',
    );
    assert.equal(runtime.requests[0]?.directory, join(root, 'app'));
    const [execution] = controlPlane.projection.executions();
    assert.equal(execution?.directory, join(root, 'app'));
    await controlPlane.close();
  });

  test('a project without a folder cannot start work that needs one, but the mock runtime can', async () => {
    const { controlPlane, time, commands, submit, createProject, createWorkstream, start } =
      setup('no-folder');
    const { projectId } = createProject(null);
    const workstreamId = createWorkstream(projectId as ProjectId);
    const refused = start(workstreamId);
    assert.equal(refused.command?.rejection?.code, 'location_required');
    const mock = submit(commands.startExecution(workstreamId, FEATURE));
    assert.equal(mock.disposition, 'accepted');
    await time.runUntilIdle();
    assert.equal(controlPlane.projection.executions()[0]?.directory, null, 'the mock uses none');
    await controlPlane.close();
  });

  test('a folder that went missing, or became a link, refuses the start until the project is bound again', async () => {
    const { controlPlane, time, commands, root, submit, createProject, createWorkstream, start } =
      setup('moved');
    const { projectId } = createProject({ kind: 'existing_folder', root, folder_name: 'app' });
    const workstreamId = createWorkstream(projectId as ProjectId);

    // The person moved the folder on the Mac.
    rmSync(join(root, 'app'), { recursive: true });
    mkdirSync(join(root, 'app-moved'));
    assert.equal(start(workstreamId).command?.rejection?.code, 'location_missing');

    // Something put a link to another folder where the project's folder was.
    symlinkSync(join(root, 'app-moved'), join(root, 'app'));
    const linked = start(workstreamId);
    assert.equal(linked.command?.rejection?.code, 'location_missing');
    assert.match(linked.command?.rejection?.message ?? '', /now leads to/);

    // Binding the project again to where the folder is now lets work start there.
    const rebound = submit(
      commands.setLocation(projectId as ProjectId, {
        kind: 'existing_folder',
        root,
        folder_name: 'app-moved',
      }),
    );
    assert.equal(rebound.command?.status, 'completed');
    const started = start(workstreamId);
    assert.equal(started.disposition, 'accepted');
    await time.runUntilIdle();
    assert.equal(controlPlane.projection.executions()[0]?.directory, join(root, 'app-moved'));
    // A location that cannot be used is refused, and the project keeps the one it has.
    const refused = submit(
      commands.setLocation(projectId as ProjectId, {
        kind: 'existing_folder',
        root,
        folder_name: 'nowhere',
      }),
    );
    assert.equal(refused.command?.rejection?.code, 'location_missing');
    assert.equal(
      controlPlane.projection.project(projectId ?? '')?.location?.path,
      join(root, 'app-moved'),
    );
    await controlPlane.close();
  });

  test('a folder removed after admission fails the start at the runtime, with no effect', async () => {
    const { controlPlane, time, root, createProject, createWorkstream, start } =
      setup('removed-after-admission');
    const { projectId } = createProject({ kind: 'existing_folder', root, folder_name: 'app' });
    const workstreamId = createWorkstream(projectId as ProjectId);
    const started = start(workstreamId);
    assert.equal(started.disposition, 'accepted');
    // The runtime is called after submit returns; the folder goes in between.
    rmSync(join(root, 'app'), { recursive: true });
    await time.runUntilIdle();
    const command = controlPlane.projection.command(started.command?.command_id ?? '');
    assert.equal(command?.status, 'failed');
    assert.equal(command?.failure?.code, 'location_missing');
    assert.equal(command?.failure?.effect, 'none');
    const [execution] = controlPlane.projection.executions();
    assert.equal(execution?.status, 'failed', 'a start that never happened, not an unknown one');
    await controlPlane.close();
  });
});

describe('GET /api/locations', () => {
  test('answers the roots and folders the host allows, and nothing is journaled', async () => {
    const root = join(base, 'route');
    mkdirSync(join(root, 'app'), { recursive: true });
    const server = await startTestServer({ locations: createHostLocations([root]) });
    try {
      const head = server.journal.head();
      const response = await fetch(`${server.baseUrl}/api/locations`, {
        headers: { authorization: `Bearer ${server.token}` },
      });
      assert.equal(response.status, 200);
      const body = (await response.json()) as unknown;
      assert.ok(compileValidator(LocationsResponse)(body).ok);
      assert.deepEqual(body, {
        roots: [
          {
            path: root,
            name: 'route',
            status: 'available',
            folders: [{ name: 'app', path: join(root, 'app') }],
            folders_truncated: false,
          },
        ],
      });
      assert.equal(server.journal.head(), head);
      const anonymous = await fetch(`${server.baseUrl}/api/locations`);
      assert.equal(anonymous.status, 401);
    } finally {
      await server.stop();
    }
  });
});
