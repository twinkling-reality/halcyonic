import assert from 'node:assert/strict';
import { existsSync, mkdtempSync, readdirSync, readFileSync, rmSync, statSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { after, describe, test } from 'node:test';
import type {
  CommandEnvelope,
  ProjectId,
  RuntimeDescriptor,
  RuntimeId,
  WorkstreamId,
} from '@halcyonic/contracts';
import {
  RuntimeActionError,
  type RuntimeAdapter,
  type StartExecutionRequest,
} from '@halcyonic/runtime-core';
import { DEMO_WORKSTREAMS } from '../demo-plan.ts';
import { pageSecrets } from '../network/setup-page.ts';
import { createTestControlPlane } from '../testing/harness.ts';
import { givenSecrets, SecretStore } from './store.ts';

// A project secret's life through the control plane (ADR 0057): set on the Mac's page, kept in the
// store alone, known by name everywhere else, given to a start whose runtime takes it, taken out of
// what the runtime says, and removed. Test values only.

const base = mkdtempSync(join(tmpdir(), 'halcyonic-secrets-'));
after(() => rmSync(base, { recursive: true, force: true }));
let folders = 0;
const scratch = () => {
  folders += 1;
  return join(base, `data-${folders}`);
};

const VALUE = 'test-value-not-real-0006';
const OTHER = 'test-value-not-real-0007';
const [FEATURE] = DEMO_WORKSTREAMS as unknown as [(typeof DEMO_WORKSTREAMS)[number]];

/** A runtime that keeps each start's request and fails with the variable it was given in its words. */
function takingRuntime(runtimeId: string, takes: boolean) {
  const starts: StartExecutionRequest[] = [];
  const descriptor: RuntimeDescriptor = {
    runtime_id: runtimeId as RuntimeId,
    kind: 'stub',
    display_name: `Stub ${runtimeId}`,
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
    uses_project_location: false,
    reports_tool_activity: false,
    ...(takes && { secret_environment: true }),
  };
  const adapter: RuntimeAdapter = {
    descriptor,
    // A runtime's own variables, as each adapter names its own (Claude Code's CLAUDE_, OpenCode's ...).
    ownsEnvironmentName: (name) => name.startsWith('STUB_'),
    validateStartOptions: () => ({ ok: true }),
    startExecution: async (request) => {
      starts.push(request);
      // As a runtime that echoes what its command printed into its error text.
      throw new RuntimeActionError(
        'runtime_unavailable',
        `the command printed ${request.environment?.PROJECT_TEST_KEY ?? 'nothing'}`,
      );
    },
    close: async () => {},
  };
  return { adapter, starts };
}

function setUp() {
  const dataDir = scratch();
  const taking = takingRuntime('taking', true);
  const plain = takingRuntime('plain', false);
  const harness = createTestControlPlane({
    projectSecrets: { dataDir },
    adapters: () => [taking.adapter, plain.adapter],
  });
  const { controlPlane, commands } = harness;
  const store = controlPlane.projectSecrets;
  assert.ok(store !== null);
  const project = controlPlane.commands.submit(commands.createProject('Garden'), 'internal');
  if (project.command?.result?.kind !== 'project_created') throw new Error('no project');
  const projectId = project.command.result.project_id;
  let ids = 0;
  const page = pageSecrets({
    submit: (command, via, principal) => controlPlane.commands.submit(command, via, principal),
    store,
    projects: () =>
      controlPlane.projection.projects().map((each) => ({
        project_id: each.project_id,
        name: each.name,
        secrets: each.secrets ?? [],
      })),
    without: () => ['Claude Agent'],
    outside: () => [],
    nextId: () => {
      ids += 1;
      return `01a19000-0000-7000-8000-${String(ids).padStart(12, '0')}`;
    },
    now: () => harness.time.now(),
  });
  const start = async (runtime: 'taking' | 'plain'): Promise<WorkstreamId> => {
    const created = controlPlane.commands.submit(
      commands.createWorkstream(projectId, FEATURE),
      'internal',
    );
    if (created.command?.result?.kind !== 'workstream_created') throw new Error('no workstream');
    const workstreamId = created.command.result.workstream_id;
    controlPlane.commands.submit(
      {
        ...commands.startExecution(workstreamId, FEATURE),
        payload: {
          workstream_id: workstreamId,
          runtime_id: runtime as RuntimeId,
          instruction: 'Read the key.',
          options: {},
          model_ref: null,
        },
      },
      'internal',
    );
    await harness.time.runUntilIdle();
    return workstreamId;
  };
  const journalText = () => JSON.stringify([...harness.journal.readAll()]);
  return { harness, dataDir, store, projectId, page, start, taking, plain, journalText };
}

describe('project secrets through the control plane (ADR 0057)', () => {
  test("a set on the Mac's page keeps the value in its own file alone; the journal and the view know its name", async () => {
    const { harness, dataDir, projectId, page, journalText } = setUp();
    assert.equal(page.set(projectId, 'PROJECT_TEST_KEY', VALUE), null);
    const file = join(dataDir, 'secrets', projectId, 'PROJECT_TEST_KEY');
    assert.equal(readFileSync(file, 'utf8'), VALUE);
    assert.equal(statSync(file).mode & 0o777, 0o600);
    assert.equal(statSync(join(dataDir, 'secrets')).mode & 0o777, 0o700);
    assert.deepEqual(readdirSync(join(dataDir, 'secrets', projectId)), ['PROJECT_TEST_KEY']);
    assert.deepEqual(harness.controlPlane.projection.project(projectId)?.secrets, [
      'PROJECT_TEST_KEY',
    ]);
    const stored = [...harness.journal.readAll()].filter(
      (each) => each.event.event_type === 'project.secret.stored',
    );
    assert.deepEqual(
      stored.map((each) => each.event.payload),
      [{ name: 'PROJECT_TEST_KEY', replaced: false }],
    );
    // Setting it again replaces it, said as such; the value is still nowhere in the journal.
    assert.equal(page.set(projectId, 'PROJECT_TEST_KEY', OTHER), null);
    assert.equal(readFileSync(file, 'utf8'), OTHER);
    assert.ok(journalText().includes('"replaced":true'));
    assert.equal(journalText().includes(VALUE), false);
    assert.equal(journalText().includes(OTHER), false);
    await harness.controlPlane.close();
  });

  test('a start on a runtime that takes secrets gets them, recorded by name; one that does not gets none; the runtime’s words lose the value', async () => {
    const { harness, projectId, page, start, taking, plain, journalText } = setUp();
    assert.equal(page.set(projectId, 'PROJECT_TEST_KEY', VALUE), null);
    await start('taking');
    assert.deepEqual(taking.starts[0]?.environment, { PROJECT_TEST_KEY: VALUE });
    const created = [...harness.journal.readAll()].find(
      (each) => each.event.event_type === 'execution.created',
    );
    const payload = created?.event.payload as { secret_names?: unknown } | undefined;
    assert.deepEqual(payload?.secret_names, ['PROJECT_TEST_KEY']);
    await start('plain');
    assert.equal(plain.starts[0]?.environment, undefined);
    // The runtime repeated the value in its error: journaled as which secret it was.
    assert.equal(journalText().includes(VALUE), false);
    assert.ok(journalText().includes('the command printed [redacted: PROJECT_TEST_KEY]'));
    await harness.controlPlane.close();
  });

  test('a removed secret is deleted and reaches no later start', async () => {
    const { harness, dataDir, projectId, page, start, taking } = setUp();
    assert.equal(page.set(projectId, 'PROJECT_TEST_KEY', VALUE), null);
    assert.equal(page.remove(projectId, 'PROJECT_TEST_KEY'), null);
    assert.equal(existsSync(join(dataDir, 'secrets', projectId, 'PROJECT_TEST_KEY')), false);
    assert.equal(harness.controlPlane.projection.project(projectId)?.secrets, undefined);
    await start('taking');
    assert.equal(taking.starts[0]?.environment, undefined);
    assert.equal(
      page.remove(projectId, 'PROJECT_TEST_KEY'),
      'This project has no secret named PROJECT_TEST_KEY.',
    );
    await harness.controlPlane.close();
  });

  test('a set sent anywhere but the Mac’s page, so with no value staged for it, keeps nothing', async () => {
    const { harness, dataDir, projectId } = setUp();
    const sent = harness.controlPlane.commands.submit(
      {
        schema_version: 1,
        command_id: '01a19000-0000-7000-8000-0000000000d1',
        issued_at: harness.time.now().toISOString(),
        client: { name: 'headset', version: null, device_label: null },
        command_type: 'project.secret.set',
        payload: { project_id: projectId, name: 'PROJECT_TEST_KEY' },
      } as CommandEnvelope,
      'websocket',
      { kind: 'local' },
    );
    assert.equal(sent.command?.status, 'failed');
    assert.equal(
      sent.command?.failure?.message,
      'A secret is set with its value in its own request, so this command alone sets nothing.',
    );
    assert.equal(harness.controlPlane.projection.project(projectId)?.secrets, undefined);
    assert.deepEqual(readdirSync(join(dataDir, 'secrets')), []);
    await harness.controlPlane.close();
  });

  test('a start is never given a name its runtime reads for itself, even one kept before the runtime said so', () => {
    const { harness, projectId, store } = setUp();
    const runtime = harness.controlPlane.registry.get('taking');
    assert.ok(runtime !== undefined);
    // Kept as the journal holds it, as a store before this runtime declared its names would have.
    store.stage(
      '01a19000-0000-7000-8000-0000000000c1',
      projectId,
      'PROJECT_TEST_KEY' as never,
      VALUE,
    );
    store.commit('01a19000-0000-7000-8000-0000000000c1', projectId, 'PROJECT_TEST_KEY' as never);
    const projection = { project: () => ({ secrets: ['PROJECT_TEST_KEY'] }) } as never;
    const withheld: number[] = [];
    assert.deepEqual(Object.keys(givenSecrets(store, projection, projectId, runtime)), [
      'PROJECT_TEST_KEY',
    ]);
    assert.deepEqual(
      givenSecrets(
        store,
        projection,
        projectId,
        runtime,
        (name) => name === 'PROJECT_TEST_KEY',
        (count) => withheld.push(count),
      ),
      {},
    );
    assert.deepEqual(withheld, [1], 'how many were held back is said');
  });

  test('a reserved name, a short value and an unknown project are refused in words, and nothing stays staged', async () => {
    const { harness, dataDir, projectId, page, store } = setUp();
    assert.match(
      page.set(projectId, 'PATH', VALUE) ?? '',
      /PATH is a name the Mac or an agent uses/,
    );
    assert.match(
      page.set(projectId, 'STUB_TOKEN', VALUE) ?? '',
      /STUB_TOKEN is a name .* a secret can't take it/,
    );
    assert.match(page.set(projectId, 'lower', VALUE) ?? '', /capital letters, digits and _/);
    assert.match(page.set(projectId, 'PROJECT_TEST_KEY', 'short') ?? '', /at least 8 characters/);
    assert.equal(
      page.set('01a19000-0000-7000-8000-0000000000e1', 'PROJECT_TEST_KEY', VALUE),
      'That project is not on this Mac.',
    );
    assert.deepEqual(store.held(), []);
    assert.deepEqual(readdirSync(join(dataDir, 'secrets')), []);
    await harness.controlPlane.close();
  });

  test('a task still running with a value it was given has it redacted after a replace and after a Remove (AUDIT3 S1 finding)', async () => {
    const sinks: StartExecutionRequest['emit'][] = [];
    let n = 0;
    const observe = (sink: StartExecutionRequest['emit'], type: string, payload: unknown) => {
      n += 1;
      sink({
        type,
        occurred_at: '2026-11-11T16:00:00.000Z',
        native_event_id: `e-${n}`,
        sequence: null,
        provenance: { epistemic: 'reported', native_type: null },
        payload,
      } as never);
    };
    const running: RuntimeAdapter = {
      descriptor: { ...takingRuntime('emitting', true).adapter.descriptor },
      validateStartOptions: () => ({ ok: true }),
      startExecution: async (request) => {
        sinks.push(request.emit);
        observe(request.emit, 'runtime.execution.started', { native_id: `s-${n}` });
        observe(request.emit, 'runtime.turn.started', { turn_id: 't1' });
        return { native_id: `s-${n}` };
      },
      close: async () => {},
    };
    const say = (text: string) => {
      for (const sink of sinks) observe(sink, 'runtime.agent_message', { text });
    };
    const harness = createTestControlPlane({
      projectSecrets: { dataDir: scratch() },
      adapters: () => [running],
    });
    const { controlPlane, commands } = harness;
    const store = controlPlane.projectSecrets;
    assert.ok(store !== null);
    const made = controlPlane.commands.submit(commands.createProject('Garden'), 'internal');
    if (made.command?.result?.kind !== 'project_created') throw new Error('no project');
    const projectId = made.command.result.project_id;
    let ids = 100;
    const page = pageSecrets({
      submit: (command, via, principal) => controlPlane.commands.submit(command, via, principal),
      store,
      projects: () =>
        controlPlane.projection.projects().map((each) => ({
          project_id: each.project_id,
          name: each.name,
          secrets: each.secrets ?? [],
        })),
      without: () => [],
      outside: () => [],
      nextId: () => {
        ids += 1;
        return `01a19000-0000-7000-8000-${String(ids).padStart(12, '0')}`;
      },
      now: () => harness.time.now(),
    });
    assert.equal(page.set(projectId, 'PROJECT_TEST_KEY', VALUE), null);
    const created = controlPlane.commands.submit(
      commands.createWorkstream(projectId, FEATURE),
      'internal',
    );
    if (created.command?.result?.kind !== 'workstream_created') throw new Error('no workstream');
    const workstreamId = created.command.result.workstream_id;
    controlPlane.commands.submit(
      {
        ...commands.startExecution(workstreamId, FEATURE),
        payload: {
          workstream_id: workstreamId,
          runtime_id: 'emitting' as RuntimeId,
          instruction: 'Read the key.',
          options: {},
          model_ref: null,
        },
      },
      'internal',
    );
    await harness.time.runUntilIdle();
    const journalText = () => JSON.stringify([...harness.journal.readAll()]);
    say(`while set: ${VALUE}`);
    assert.equal(page.set(projectId, 'PROJECT_TEST_KEY', OTHER), null);
    say(`after replace: ${VALUE}`);
    assert.equal(page.remove(projectId, 'PROJECT_TEST_KEY'), null);
    say(`after remove: ${OTHER}`);
    await harness.time.runUntilIdle();
    const text = journalText();
    assert.equal(text.includes(VALUE), false, 'the first value is never journaled');
    assert.equal(text.includes(OTHER), false, 'the second value is never journaled');
    for (const said of ['while set', 'after replace', 'after remove'])
      assert.ok(text.includes(`${said}: [redacted: PROJECT_TEST_KEY]`), said);
    await controlPlane.close();
  });

  test('a restart keeps only the values the journal lists, and drops what a press left staged', () => {
    const dataDir = scratch();
    const project = '01a19000-0000-7000-8000-0000000000f1' as ProjectId;
    const first = new SecretStore(dataDir);
    first.open(() => []);
    first.stage('01a19000-0000-7000-8000-0000000000f2', project, 'KEPT_KEY', VALUE);
    assert.ok(first.commit('01a19000-0000-7000-8000-0000000000f2', project, 'KEPT_KEY'));
    first.stage('01a19000-0000-7000-8000-0000000000f3', project, 'NEVER_RECORDED', OTHER);
    assert.ok(first.commit('01a19000-0000-7000-8000-0000000000f3', project, 'NEVER_RECORDED'));
    first.stage('01a19000-0000-7000-8000-0000000000f4', project, 'LEFT_STAGED', OTHER);
    // Known to redaction from the moment it is staged.
    assert.deepEqual(
      first
        .held()
        .map((each) => each.what)
        .sort(),
      ['KEPT_KEY', 'LEFT_STAGED', 'NEVER_RECORDED'],
    );
    const again = new SecretStore(dataDir);
    again.open(() => ['KEPT_KEY']);
    assert.deepEqual(readdirSync(join(dataDir, 'secrets', project)), ['KEPT_KEY']);
    assert.deepEqual(again.environment(project, ['KEPT_KEY', 'NEVER_RECORDED']), {
      KEPT_KEY: VALUE,
    });
    assert.deepEqual(again.held(), [{ what: 'KEPT_KEY', value: VALUE }]);
  });
});
