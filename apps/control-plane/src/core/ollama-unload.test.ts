import assert from 'node:assert/strict';
import { createServer, type IncomingMessage, type ServerResponse } from 'node:http';
import type { AddressInfo } from 'node:net';
import { describe, type TestContext, test } from 'node:test';
import type { ExecutionId } from '@halcyonic/contracts';
import { MOCK_MODELS, MockRuntimeAdapter } from '@halcyonic/integration-mock';
import { createVirtualTime } from '@halcyonic/runtime-core';
import { createCommandFactory, DEMO_WORKSTREAMS } from '../demo-plan.ts';
import { createUuidV7Generator } from '../ids.ts';
import { openSqliteJournal } from '../journal/sqlite-journal.ts';
import { capturingLogger, SCENARIOS, TEST_CLIENT } from '../testing/harness.ts';
import { ControlPlane } from './control-plane.ts';
import { controlPlaneDraft, NO_CAUSE } from './drafts.ts';
import {
  normalizeOllamaTag,
  OllamaUnloader,
  ollamaModelFrom,
  sameOllamaModel,
} from './ollama-unload.ts';

const [FEATURE] = DEMO_WORKSTREAMS as unknown as [(typeof DEMO_WORKSTREAMS)[number]];

interface StandInCall {
  readonly method: string;
  readonly path: string;
  readonly body: string;
}

/** A loopback HTTP stand-in for Ollama's `/api/ps` and `/api/generate` on an ephemeral port. */
async function standInOllama(
  t: TestContext,
  initialLoaded: readonly string[] = [],
  options: { psStatus?: number } = {},
) {
  const loaded = new Set<string>(initialLoaded.map(normalizeOllamaTag));
  const calls: StandInCall[] = [];
  const unloaded: string[] = [];
  const server = createServer((req: IncomingMessage, res: ServerResponse) => {
    const chunks: Buffer[] = [];
    req.on('data', (chunk: Buffer) => chunks.push(chunk));
    req.on('end', () => {
      const body = Buffer.concat(chunks).toString('utf8');
      calls.push({ method: req.method ?? '', path: req.url ?? '', body });
      if (req.url === '/api/ps') {
        const status = options.psStatus ?? 200;
        res.writeHead(status, { 'content-type': 'application/json' });
        res.end(
          JSON.stringify({
            models: [...loaded].map((model) => ({ name: model, model })),
          }),
        );
        return;
      }
      if (req.url === '/api/generate' && req.method === 'POST') {
        const parsed = JSON.parse(body) as { model?: string; keep_alive?: number };
        if (typeof parsed.model === 'string' && parsed.keep_alive === 0) {
          unloaded.push(parsed.model);
          loaded.delete(normalizeOllamaTag(parsed.model));
        }
        res.writeHead(200, { 'content-type': 'application/json' });
        res.end(JSON.stringify({ done: true }));
        return;
      }
      res.writeHead(404);
      res.end();
    });
  });
  await new Promise<void>((resolve) => server.listen(0, '127.0.0.1', () => resolve()));
  t.after(
    () =>
      new Promise<void>((resolve) => {
        server.closeAllConnections();
        server.close(() => resolve());
      }),
  );
  const { port } = server.address() as AddressInfo;
  return {
    url: `http://127.0.0.1:${port}`,
    loaded,
    calls,
    unloaded,
  };
}

describe('extracting and normalizing Ollama model names (UL1)', () => {
  test('strips the ollama/ prefix from model_ref or options and normalizes tags without a colon to :latest', () => {
    assert.equal(ollamaModelFrom('ollama/muse-glimmer:30b-mlx'), 'muse-glimmer:30b-mlx');
    assert.equal(ollamaModelFrom('ollama/qwen3.6'), 'qwen3.6');
    assert.equal(ollamaModelFrom('openai/gpt-5.5'), null);
    assert.equal(
      ollamaModelFrom(null, { model: 'ollama/qwen3.6:35b-a3b-nvfp4' }),
      'qwen3.6:35b-a3b-nvfp4',
    );
    assert.equal(
      ollamaModelFrom(null, { model_provider: 'ollama', model: 'qwen3.6:35b' }),
      'qwen3.6:35b',
    );
    assert.equal(normalizeOllamaTag('qwen3.6'), 'qwen3.6:latest');
    assert.equal(normalizeOllamaTag('qwen3.6:35b-a3b-nvfp4'), 'qwen3.6:35b-a3b-nvfp4');
    assert.equal(sameOllamaModel('qwen3.6', 'qwen3.6:latest'), true);
    assert.equal(sameOllamaModel('qwen3.6:35b', 'qwen3.6:latest'), false);
    assert.throws(() => new OllamaUnloader({ url: 'https://ollama.com' }), /loopback/);
  });
});

describe('unloading a local Ollama model that Halcyonic loaded when the execution ends (UL1)', () => {
  test('unloads a model that was not in /api/ps before the execution started, and leaves one that was already loaded alone', async (t) => {
    const ollama = await standInOllama(t, ['already-here:latest']);
    const time = createVirtualTime(new Date('2026-10-31T12:00:00.000Z'));
    const ids = createUuidV7Generator({ now: () => time.now().getTime() });
    const journal = openSqliteJournal({ path: ':memory:', originIfNew: 'live', ids });
    const ollamaUnload = new OllamaUnloader({ url: ollama.url });
    const controlPlane = new ControlPlane({
      journal,
      adapters: [
        new MockRuntimeAdapter({
          scenarios: SCENARIOS,
          models: [
            ...MOCK_MODELS,
            {
              model_ref: 'ollama/muse-glimmer:30b-mlx',
              display_name: 'Muse Glimmer',
              served: 'this_mac',
              tool_calling: 'declared',
              context_tokens: 131_072,
            },
            {
              model_ref: 'ollama/already-here',
              display_name: 'Already Here',
              served: 'this_mac',
              tool_calling: 'declared',
              context_tokens: 131_072,
            },
          ],
          clock: time,
          scheduler: time,
        }),
      ],
      ids,
      clock: time,
      scheduler: time,
      logger: capturingLogger().logger,
      commandTimeoutMs: 30_000,
      ollamaUnload,
    });
    t.after(() => controlPlane.close());
    const commands = createCommandFactory(
      createUuidV7Generator({ now: () => time.now().getTime() }),
      time,
      TEST_CLIENT,
    );
    const project = controlPlane.commands.submit(commands.createProject('Garden'), 'internal');
    assert.equal(project.command?.result?.kind, 'project_created');
    const projectId =
      project.command?.result?.kind === 'project_created' ? project.command.result.project_id : '';
    const ws = controlPlane.commands.submit(
      commands.createWorkstream(projectId as never, FEATURE),
      'internal',
    );
    const workstreamId =
      ws.command?.result?.kind === 'workstream_created' ? ws.command.result.workstream_id : '';

    // 1. A model not in /api/ps before start is unloaded once the turn completes.
    const start1 = commands.startExecution(workstreamId as never, FEATURE);
    const submitted1 = controlPlane.commands.submit(
      { ...start1, payload: { ...start1.payload, model_ref: 'ollama/muse-glimmer:30b-mlx' } },
      'internal',
    );
    assert.equal(submitted1.disposition, 'accepted');
    await time.runUntilIdle();
    await new Promise((resolve) => setTimeout(resolve, 40));
    assert.deepEqual(ollama.unloaded, ['muse-glimmer:30b-mlx']);

    // 2. A model already in /api/ps before start (matched across `already-here` vs `already-here:latest`) is NOT unloaded.
    const start2 = commands.startExecution(workstreamId as never, FEATURE);
    const submitted2 = controlPlane.commands.submit(
      { ...start2, payload: { ...start2.payload, model_ref: 'ollama/already-here' } },
      'internal',
    );
    assert.equal(submitted2.disposition, 'accepted');
    await time.runUntilIdle();
    await new Promise((resolve) => setTimeout(resolve, 40));
    assert.deepEqual(ollama.unloaded, ['muse-glimmer:30b-mlx']);
  });

  test('waits until every active execution using the model has ended before unloading it, and never unloads when /api/ps could not be read', async (t) => {
    const ollama = await standInOllama(t, []);
    const unloader = new OllamaUnloader({ url: ollama.url });
    const e1 = '01a19000-0000-7000-8000-0000000000e1' as ExecutionId;
    const e2 = '01a19000-0000-7000-8000-0000000000e2' as ExecutionId;

    await unloader.beforeRun(e1, 'qwen3.6:35b-a3b-nvfp4');
    await unloader.beforeRun(e2, 'qwen3.6:35b-a3b-nvfp4');
    await unloader.executionEnded(e1);
    assert.deepEqual(ollama.unloaded, [], 'still used by e2');
    await unloader.executionEnded(e2);
    assert.deepEqual(ollama.unloaded, ['qwen3.6:35b-a3b-nvfp4']);
    // Only one /api/ps read was needed for the overlapping pair.
    assert.equal(ollama.calls.filter((call) => call.path === '/api/ps').length, 1);

    // When /api/ps fails (e.g. HTTP 500 or unreachable), preloaded is unknown so nothing is unloaded.
    const failing = await standInOllama(t, [], { psStatus: 500 });
    const cautious = new OllamaUnloader({ url: failing.url });
    const e3 = '01a19000-0000-7000-8000-0000000000e3' as ExecutionId;
    await cautious.beforeRun(e3, 'qwen3.6:35b-a3b-nvfp4');
    await cautious.executionEnded(e3);
    assert.deepEqual(failing.unloaded, []);
  });

  test('an execution that switches models or becomes unknown releases its model, and close() unloads what is still held', async (t) => {
    const ollama = await standInOllama(t, []);
    const time = createVirtualTime(new Date('2026-10-31T12:05:00.000Z'));
    const ids = createUuidV7Generator({ now: () => time.now().getTime() });
    const journal = openSqliteJournal({ path: ':memory:', originIfNew: 'live', ids });
    const ollamaUnload = new OllamaUnloader({ url: ollama.url });
    const controlPlane = new ControlPlane({
      journal,
      adapters: [],
      ids,
      clock: time,
      scheduler: time,
      logger: capturingLogger().logger,
      commandTimeoutMs: 30_000,
      ollamaUnload,
    });
    const projectId = '01a19000-0000-7000-8000-000000000001' as never;
    const workstreamId = '01a19000-0000-7000-8000-0000000000a1' as never;
    const executionId = '01a19000-0000-7000-8000-0000000000e9' as ExecutionId;
    const scope = { project_id: projectId, workstream_id: workstreamId, execution_id: executionId };
    const now = '2026-10-31T12:05:00.000Z' as never;
    controlPlane.recorder.record(
      controlPlaneDraft(
        'project.created',
        { project_id: projectId, workstream_id: null, execution_id: null },
        { name: 'Garden', location: null },
        now,
        NO_CAUSE,
      ),
    );
    controlPlane.recorder.record(
      controlPlaneDraft(
        'workstream.created',
        { project_id: projectId, workstream_id: workstreamId, execution_id: null },
        { title: 'Task', objective: null },
        now,
        NO_CAUSE,
      ),
    );
    controlPlane.recorder.record(
      controlPlaneDraft(
        'execution.created',
        scope,
        {
          runtime: { runtime_id: 'codex', kind: 'codex', display_name: 'Codex', synthetic: false },
          instruction: 'Go.',
          directory: null,
        } as never,
        now,
        NO_CAUSE,
      ),
    );
    await ollamaUnload.beforeRun(executionId, 'qwen3.6:35b-a3b-nvfp4');
    await controlPlane.close();
    assert.deepEqual(ollama.unloaded, ['qwen3.6:35b-a3b-nvfp4']);
  });
});
