import assert from 'node:assert/strict';
import { after, before, describe, test } from 'node:test';
import type { RuntimeDescriptor, ServerMessage, WorkstreamView } from '@halcyonic/contracts';
import { MOCK_MODELS, MockRuntimeAdapter } from '@halcyonic/integration-mock';
import { systemClock } from '@halcyonic/runtime-core';
import { RealtimeClient } from './client/realtime-client.ts';
import { createCommandFactory, DEMO_WORKSTREAMS, MOCK_RUNTIME_ID } from './demo-plan.ts';
import {
  chooseModel,
  listModels,
  parseDemoArguments,
  readJson,
  startScenario,
  submit,
  workstreamLine,
} from './demo-scenario.ts';
import { createUuidV7Generator } from './ids.ts';
import { SCENARIOS, startTestServer } from './testing/harness.ts';

describe('pnpm demo --scenario', () => {
  test('reads one scenario name, or none for the plan', () => {
    assert.deepEqual(parseDemoArguments([]), { scenario: null });
    assert.deepEqual(parseDemoArguments(['--scenario', 'question_asked']), {
      scenario: 'question_asked',
    });
    assert.ok('error' in parseDemoArguments(['--scenario']));
    assert.ok('error' in parseDemoArguments(['--scenario', '../x']));
    assert.ok('error' in parseDemoArguments(['question_asked']));
    assert.ok('error' in parseDemoArguments(['--scenario', 'a', 'b']));
  });

  test('chooses a model on this Mac that declares tools, as a start must name one', () => {
    const listed = { model_choice: 'listed' } as RuntimeDescriptor;
    assert.equal(chooseModel(listed, MOCK_MODELS), 'mock/fast');
    assert.equal(
      chooseModel(
        listed,
        MOCK_MODELS.filter((model) => model.model_ref !== 'mock/fast'),
      ),
      'mock/no-tools',
    );
    assert.equal(chooseModel({ model_choice: 'none' } as RuntimeDescriptor, MOCK_MODELS), null);
  });

  describe('against a control plane whose mock lists models, as pnpm dev runs it', () => {
    let server: Awaited<ReturnType<typeof startTestServer>>;
    let client: RealtimeClient;
    const clientInfo = { name: 'halcyonic-demo', version: null, device_label: null };
    const commands = createCommandFactory(createUuidV7Generator(), systemClock, clientInfo);

    before(async () => {
      server = await startTestServer({
        adapters: (time) => [
          new MockRuntimeAdapter({
            scenarios: SCENARIOS,
            clock: time,
            scheduler: time,
            models: MOCK_MODELS,
          }),
        ],
      });
      client = await RealtimeClient.connect(server.wsUrl, server.token);
      client.hello(clientInfo);
      await client.waitFor((message) => message.type === 'snapshot');
    });
    after(async () => {
      await client.close();
      await server.stop();
    });

    test('a start that names no model is refused, as the demo sent before', async () => {
      const project = await submit(client, commands.createProject('No model'));
      assert.equal(project.result?.kind, 'project_created');
      if (project.result?.kind !== 'project_created') return;
      const plan = DEMO_WORKSTREAMS[0];
      assert.ok(plan);
      const created = await submit(
        client,
        commands.createWorkstream(project.result.project_id, plan),
      );
      if (created.result?.kind !== 'workstream_created') throw new Error('not created');
      await assert.rejects(
        submit(
          client,
          commands.startExecution(created.result.workstream_id, plan, MOCK_RUNTIME_ID),
        ),
        /Choose a model/,
      );
    });

    test('starts the scenario and leaves its question waiting for the person', async () => {
      const mock = server.controlPlane
        .snapshot()
        .runtimes.find((runtime) => runtime.runtime_id === 'mock');
      assert.ok(mock);
      const models = await listModels(server.baseUrl, server.token, 'mock');
      const { workstreamId } = await startScenario({
        client,
        commands,
        name: 'question_asked',
        mock,
        models,
      });
      await server.time.runUntilIdle();
      const workstream = server.controlPlane
        .snapshot()
        .workstreams.find((each) => each.workstream_id === workstreamId);
      assert.equal(workstream?.status, 'waiting_for_human');
      const execution = server.controlPlane
        .snapshot()
        .executions.find((each) => each.workstream_id === workstreamId);
      assert.equal(execution?.pending_questions.length, 1, 'nothing answered it');
      assert.equal(execution?.runtime.runtime_id, 'mock');
    });

    test('an unknown scenario is refused in words', async () => {
      const mock = server.controlPlane
        .snapshot()
        .runtimes.find((runtime) => runtime.runtime_id === 'mock');
      assert.ok(mock);
      await assert.rejects(
        startScenario({ client, commands, name: 'no_such_scenario', mock, models: MOCK_MODELS }),
        /execution\.start was not accepted/,
      );
    });
  });
});

describe('what pnpm demo prints', () => {
  const message = {
    type: 'event',
    position: 7,
    event: { event_type: 'workstream.created' },
  } as unknown as Extract<ServerMessage, { type: 'event' }>;
  const workstream = (projectId: string, title: string) =>
    ({
      workstream_id: 'w1',
      project_id: projectId,
      title,
      status: 'idle',
      attention: { level: 'none', reasons: [] },
    }) as unknown as WorkstreamView;

  test("only the demo's own project, never the person's own work", () => {
    assert.match(
      workstreamLine(workstream('demo', 'Add a login page'), message, new Map(), 'demo') ?? '',
      /Add a login page/,
    );
    assert.equal(
      workstreamLine(workstream('theirs', 'PRIVATE-TITLE'), message, new Map(), 'demo'),
      null,
    );
    assert.equal(
      workstreamLine(workstream('theirs', 'PRIVATE-TITLE'), message, new Map(), null),
      null,
      'before its project exists',
    );
  });

  test('an answer that is not JSON fails in fixed words, never quoting it', async () => {
    await assert.rejects(readJson(new Response('x SECRETBODY }')), (error: unknown) => {
      assert.equal((error as Error).message, 'The control plane gave an answer that is not JSON.');
      return true;
    });
    assert.deepEqual(await readJson(new Response('{"a":1}')), { a: 1 });
    assert.equal(await readJson(new Response('')), null);
  });
});
