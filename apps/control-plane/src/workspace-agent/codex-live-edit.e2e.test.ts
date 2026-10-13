import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { existsSync, readFileSync } from 'node:fs';
import { writeFile } from 'node:fs/promises';
import { join } from 'node:path';
import { test } from 'node:test';
import { setTimeout as delay } from 'node:timers/promises';
import type { CommandId, CommandOf, ExecutionId } from '@halcyonic/contracts';
import { CodexRuntimeAdapter } from '@halcyonic/integration-codex';
import { PATCH_CONTENT } from '../../../../packages/integrations/codex/src/testing/fake-provider.ts';
import { createSandbox } from '../../../../packages/integrations/codex/src/testing/sandbox.ts';
import { ToolResults } from '../core/tool-results.ts';
import { DEMO_WORKSTREAMS } from '../demo-plan.ts';
import { createUuidV7Generator } from '../ids.ts';
import { createHostLocations } from '../locations.ts';
import { capturingLogger, createTestControlPlane } from '../testing/harness.ts';
import type { ToolMessage, ToolTurnResult } from './ollama.ts';
import { WorkspaceRequests } from './requests.ts';

const binary = process.env.CODEX_BIN ?? '';
const skip =
  binary === '' || !existsSync(binary) ? 'CODEX_BIN must name the pinned real Codex binary' : false;
const unavailable = {
  availability: 'unavailable' as const,
  reason: { code: 'synthetic_unavailable', message: 'No external source in this witness.' },
};

async function until(current: () => boolean, description: string): Promise<void> {
  const deadline = Date.now() + 30_000;
  while (!current()) {
    assert.ok(Date.now() < deadline, `Timed out waiting for ${description}.`);
    await delay(20);
  }
}

/** Real scoped coordinator and real pinned runtime; every model response and repository is synthetic. */
test('workspace request edits an approved synthetic repository and observes a real offline command through Codex', {
  skip,
  timeout: 120_000,
}, async (t) => {
  const sandbox = await createSandbox(binary);
  const locations = createHostLocations([sandbox.project]);
  const runtime = new CodexRuntimeAdapter({
    binaryPath: binary,
    codexHome: sandbox.codexHome,
    serverRecordFile: sandbox.recordFile,
    directoryPolicy: locations.policy,
    env: sandbox.env,
  });
  const logs = capturingLogger();
  const h = createTestControlPlane({ adapters: () => [runtime], locations, logger: logs.logger });
  let coordinator: WorkspaceRequests | undefined;
  t.after(async () => {
    coordinator?.close();
    try {
      await h.controlPlane.close();
    } finally {
      await sandbox.cleanup();
    }
    assert.deepEqual(
      sandbox.egress,
      [],
      'A runtime process tried to leave loopback through the proxy.',
    );
    assert.deepEqual(sandbox.sockets, [], 'A runtime process held a socket beyond loopback.');
  });
  await writeFile(
    join(sandbox.project, 'check.mjs'),
    [
      "import assert from 'node:assert/strict';",
      "import { appendFileSync, readFileSync, writeFileSync } from 'node:fs';",
      `assert.equal(readFileSync('changed.txt', 'utf8'), ${JSON.stringify(`${PATCH_CONTENT}\n`)});`,
      "appendFileSync('check-runs.txt', 'checked\\n');",
      "writeFileSync('check-passed.txt', 'synthetic offline check passed\\n');",
      "console.log('synthetic offline command finished');",
    ].join('\n'),
  );
  execFileSync('git', ['init', '--quiet', sandbox.project], { stdio: 'ignore' });
  assert.equal(existsSync(join(sandbox.project, 'changed.txt')), false);
  const project = h.controlPlane.commands.submit(
    h.commands.createProject('Synthetic live edit', {
      kind: 'existing_folder',
      root: sandbox.project,
      folder_name: null,
    }),
    'http',
    { kind: 'local' },
  );
  assert.ok(project.command?.result?.kind === 'project_created');
  const projectId = project.command.result.project_id;
  const scenario = DEMO_WORKSTREAMS[0];
  assert.ok(scenario);
  const work = h.controlPlane.commands.submit(
    h.commands.createWorkstream(projectId, {
      ...scenario,
      title: 'Synthetic edit and offline check',
      objective: 'Edit one fixture and run its deterministic local check.',
    }),
    'http',
    { kind: 'local' },
  );
  assert.ok(work.command?.result?.kind === 'workstream_created');
  const workId = work.command.result.workstream_id;
  const ids = createUuidV7Generator({ now: () => h.time.now().getTime() });
  const base = () => ({
    schema_version: 1 as const,
    command_id: ids.next() as CommandId,
    issued_at: h.time.now().toISOString(),
    client: { name: 'synthetic-live-edit', version: null, device_label: null },
  });
  const selectedModel = (await runtime.listModels()).find((model) => model.served === 'this_mac');
  assert.ok(selectedModel, 'The runtime must actually list a model served on this Mac.');
  const start: CommandOf<'execution.start'> = {
    ...base(),
    command_type: 'execution.start',
    payload: {
      workstream_id: workId,
      runtime_id: runtime.descriptor.runtime_id,
      instruction: 'Synthetic setup only.',
      options: {},
      model_ref: selectedModel.model_ref,
    },
  };
  const admitted = h.controlPlane.commands.submit(start, 'http', { kind: 'local' });
  assert.equal(
    admitted.disposition,
    'accepted',
    `Runtime start refusal: ${admitted.command?.rejection?.code ?? admitted.command?.failure?.code ?? 'unavailable'}.`,
  );
  await until(() => {
    const command = h.controlPlane.projection.command(start.command_id);
    if (command?.status === 'failed' || command?.status === 'rejected')
      throw new Error(
        `Runtime start ${command.status}: ${command.failure?.code ?? 'unavailable'}.`,
      );
    return command?.status === 'completed';
  }, 'runtime start receipt');
  const executionId = h.controlPlane.projection.workstream(workId)
    ?.current_execution_id as ExecutionId;
  assert.ok(executionId);
  await until(
    () =>
      [...h.journal.readAll()].some(
        (stored) =>
          stored.event.execution_id === executionId &&
          stored.event.event_type === 'runtime.turn.completed' &&
          stored.event.provenance.epistemic === 'observed',
      ),
    'observed setup native turn end',
  );
  const node = `'${process.execPath.replaceAll("'", "'\\''")}'`;
  const directions = ['PATCH_ADD:changed.txt', `CMD:${node} check.mjs`];
  let turns = 0;
  let readError: unknown;
  const observed: unknown[] = [];
  coordinator = new WorkspaceRequests({
    controlPlane: h.controlPlane,
    ids,
    understanding: { understand: async () => unavailable },
    evaluation: { evaluate: async () => unavailable },
    model: {
      status: async () => ({
        availability: 'available',
        model: 'synthetic-coordinator',
        served: 'this_mac',
      }),
      turn: async (messages: readonly ToolMessage[]): Promise<ToolTurnResult> => {
        const continuation = directions[turns++];
        if (turns === 4) {
          try {
            const read = messages.findLast(
              (message) => message.role === 'tool' && message.tool_name === 'read_result',
            );
            assert.ok(read);
            const data = JSON.parse(read.content);
            assert.equal(data.kind, 'command');
            assert.equal(data.completion, 'completed');
            assert.equal(data.exit_code, 0);
            assert.equal(data.availability, 'available');
            assert.equal(data.text, 'synthetic offline command finished\n');
            assert.ok(Buffer.byteLength(read.content) <= 1024);
            assert.equal(
              Object.hasOwn(data, 'excerpt'),
              false,
              'Typed fields, never a JSON-prefix excerpt.',
            );
          } catch (error) {
            readError = error;
            throw error;
          }
        }
        if (turns > 1) {
          const receipts = h.controlPlane.projection
            .workspaceRequests()
            .flatMap((request) =>
              h.controlPlane.projection.workspacePreparedActions(request.request_id),
            );
          assert.equal(receipts.length, Math.min(turns - 1, 2));
          assert.ok(
            receipts.every(
              (action) =>
                action.step.command_status === 'completed' &&
                action.step.runtime_status === 'completed' &&
                action.step.status === 'confirmed',
            ),
          );
          observed.push(
            ...messages.filter(
              (message) =>
                message.role === 'system' && message.content.startsWith('Fresh host receipts'),
            ),
          );
        }
        return {
          kind: 'answered',
          content:
            continuation === undefined && turns !== 3
              ? 'Synthetic reported answer; installed build remains unknown.'
              : '',
          toolCalls:
            turns === 3
              ? [
                  { function: { name: 'read_results', arguments: {} } },
                  {
                    function: {
                      name: 'read_result',
                      arguments: {
                        event_id: h.controlPlane.projection
                          .executionResultSummaries(executionId)
                          .summaries.find((item) => item.kind === 'command')?.event_id,
                      },
                    },
                  },
                ]
              : continuation === undefined
                ? []
                : [{ function: { name: 'continue_task', arguments: { text: continuation } } }],
          model: 'synthetic-coordinator',
          served: 'this_mac',
        };
      },
    },
  });
  const requestCommand: CommandOf<'workspace.request.submit'> = {
    ...base(),
    command_type: 'workspace.request.submit',
    payload: {
      project_id: projectId,
      workstream_id: workId,
      execution_id: executionId,
      text: 'Make the synthetic edit and check it locally. Do not build, install or open an app.',
    },
  };
  const submitted = h.controlPlane.commands.submit(requestCommand, 'http', { kind: 'local' });
  assert.ok(submitted.command?.result?.kind === 'workspace_request_created');
  const requestId = submitted.command.result.request_id;
  await until(() => {
    if (readError) throw readError;
    const status = h.controlPlane.projection.workspaceRequest(requestId)?.status;
    assert.notEqual(
      status,
      'failed',
      'The scoped synthetic coordinator failed before its final report.',
    );
    return status === 'responded';
  }, 'coordinator reported answer');
  assert.equal(turns, 4);
  const prepared = h.controlPlane.projection.workspacePreparedActions(requestId);
  assert.equal(prepared.length, 2);
  assert.ok(
    prepared.every(
      (action) =>
        action.command.command_type === 'execution.send_instruction' &&
        action.command.payload.execution_id === executionId &&
        action.step.status === 'confirmed' &&
        action.step.command_status === 'completed' &&
        action.step.runtime_status === 'completed',
    ),
  );
  assert.equal(new Set(prepared.map((action) => action.command.command_id)).size, 2);
  assert.equal(readFileSync(join(sandbox.project, 'changed.txt'), 'utf8'), `${PATCH_CONTENT}\n`);
  assert.equal(
    readFileSync(join(sandbox.project, 'check-passed.txt'), 'utf8'),
    'synthetic offline check passed\n',
  );
  assert.equal(readFileSync(join(sandbox.project, 'check-runs.txt'), 'utf8'), 'checked\n');
  const journal = [...h.journal.readAll()].filter(
    (stored) => stored.event.execution_id === executionId,
  );
  assert.ok(
    journal.some(
      (stored) =>
        stored.event.event_type === 'runtime.model.used' &&
        stored.event.payload.model_ref === selectedModel.model_ref,
    ),
    'The thread must report the exact chosen local model.',
  );
  const tools = journal.filter((stored) => stored.event.event_type === 'runtime.tool.started');
  assert.ok(
    tools.some(
      (stored) =>
        stored.event.event_type === 'runtime.tool.started' &&
        stored.event.payload.tool_name === 'fileChange',
    ),
  );
  assert.ok(
    tools.some(
      (stored) =>
        stored.event.event_type === 'runtime.tool.started' &&
        stored.event.payload.tool_name === 'commandExecution',
    ),
  );
  assert.equal(
    journal.filter((stored) => stored.event.event_type === 'runtime.tool.completed').length,
    2,
  );
  const reader = new ToolResults(h.controlPlane);
  const scope = {
    journal_id: h.journal.info.journal_id,
    project_id: projectId,
    workstream_id: workId,
  };
  const results = reader.list(executionId, scope, { kind: 'local' });
  assert.equal(results?.availability, 'available');
  assert.equal(results?.total, 2);
  for (const summary of results?.results ?? []) {
    const detail = reader.read(executionId, summary.event_id, scope, { kind: 'local' });
    assert.ok(detail);
    assert.equal(detail.result.completion, 'completed');
    if (detail?.result.kind === 'command') {
      assert.equal(detail.result.exit_code, 0);
      assert.equal(detail.result.output.availability, 'available');
    } else {
      assert.equal(detail?.result.kind, 'file_changes');
      assert.equal(detail.result.files[0]?.path, 'changed.txt');
      assert.equal(detail.result.files[0]?.patch.availability, 'available');
      assert.equal(detail.result.files[0]?.patch.text?.includes(PATCH_CONTENT), true);
    }
  }
  const readSteps = h.controlPlane.projection
    .workspaceRequest(requestId)
    ?.steps.filter((step) => step.tool_name === 'read_result' || step.tool_name === 'read_results');
  assert.equal(readSteps?.length, 2);
  assert.equal(
    readSteps?.every((step) => step.status === 'confirmed'),
    true,
  );
  assert.ok(
    journal
      .filter((stored) => stored.event.event_type === 'runtime.tool.completed')
      .every(
        (stored) =>
          stored.event.event_type === 'runtime.tool.completed' &&
          stored.event.payload.outcome === 'succeeded' &&
          stored.event.provenance.epistemic === 'observed',
      ),
  );
  assert.equal(h.controlPlane.projection.workspaceRequest(requestId)?.reply?.epistemic, 'reported');
  assert.equal(h.controlPlane.projection.execution(executionId)?.last_test_run, null);
  assert.ok(
    observed.length > 0,
    'The coordinator consumed fresh native receipts between continuations.',
  );
  assert.equal(
    sandbox.provider.requests.filter((request) => request.plan === 'custom_tool_call').length,
    1,
  );
  assert.equal(
    sandbox.provider.requests.filter((request) => request.plan === 'function_call').length,
    1,
  );
  const logged = JSON.stringify(logs.entries);
  assert.equal(
    directions.some((direction) => logged.includes(direction)),
    false,
    'Instruction text must not be logged.',
  );
  assert.equal(
    logged.includes('Synthetic reported answer'),
    false,
    'Working/model message text must not be logged.',
  );
});
