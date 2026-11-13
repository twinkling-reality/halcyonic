import assert from 'node:assert/strict';
import { randomUUID } from 'node:crypto';
import { existsSync, mkdirSync, mkdtempSync, realpathSync, rmSync } from 'node:fs';
import { createServer, type Server } from 'node:http';
import type { AddressInfo } from 'node:net';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { after, test } from 'node:test';
import { setTimeout as delay } from 'node:timers/promises';
import type { ExecutionId, ProjectId, WorkstreamId } from '@halcyonic/contracts';
import type { RuntimeObservation } from '@halcyonic/runtime-core';
import { ClaudeAgentRuntimeAdapter } from './claude-agent-runtime.ts';

/**
 * A task's spending limit, on the real CLI the agent SDK bundles (CLAUDE_CODE_BIN): a fake Messages
 * API on loopback answers every request with 100,000 input tokens (the CLI prices one at $0.401 by
 * its list prices) and a read-only tool call, so the work goes on until the CLI stops it at the limit
 * (spending-cap.md). The key is a dummy.
 */
const BINARY = process.env.CLAUDE_CODE_BIN ?? '';
const SKIP =
  BINARY === ''
    ? 'CLAUDE_CODE_BIN is not set; point it at the Claude Code binary the agent SDK bundles to run this test'
    : existsSync(BINARY)
      ? false
      : `CLAUDE_CODE_BIN does not exist: ${BINARY}`;

for (const name of ['DEBUG', 'DEBUG_SDK', 'DEBUG_CLAUDE_AGENT_SDK', 'CLAUDE_CODE_DEBUG_LOGS_DIR']) {
  delete process.env[name];
}
const ROOT = realpathSync(mkdtempSync(join(tmpdir(), 'halcyonic-claude-cap-')));
after(() => rmSync(ROOT, { recursive: true, force: true }));
for (const name of ['home', 'config', 'tmp', 'halcyonic', 'project']) mkdirSync(join(ROOT, name));

/** A Messages API whose every answer costs the CLI $0.401 and asks for one more Glob. */
function fakeApi() {
  const answered = { count: 0 };
  const server = createServer((req, res) => {
    let raw = '';
    req.on('data', (chunk) => {
      raw += chunk;
    });
    req.on('end', () => {
      if (req.method !== 'POST' || !req.url?.startsWith('/v1/messages')) {
        res.writeHead(404, { 'content-type': 'application/json' }).end('{}');
        return;
      }
      if (req.url.includes('count_tokens')) {
        res.writeHead(200, { 'content-type': 'application/json' }).end('{"input_tokens":10}');
        return;
      }
      const body = JSON.parse(raw) as { stream?: boolean; model?: string; tools?: unknown[] };
      answered.count += 1;
      const tool = (body.tools ?? []).length > 0 && answered.count < 30;
      const block = tool
        ? {
            type: 'tool_use',
            id: `toolu_${randomUUID().slice(0, 8)}`,
            name: 'Glob',
            input: { pattern: '*' },
          }
        : { type: 'text', text: 'Done.' };
      const stop = tool ? 'tool_use' : 'end_turn';
      const usage = {
        input_tokens: 100_000,
        cache_read_input_tokens: 0,
        cache_creation_input_tokens: 0,
      };
      const message = {
        id: `msg_${randomUUID()}`,
        type: 'message',
        role: 'assistant',
        model: body.model,
        stop_sequence: null,
      };
      if (body.stream !== true) {
        res.writeHead(200, { 'content-type': 'application/json' }).end(
          JSON.stringify({
            ...message,
            content: [block],
            stop_reason: stop,
            usage: { ...usage, output_tokens: 50 },
          }),
        );
        return;
      }
      res.writeHead(200, { 'content-type': 'text/event-stream', 'cache-control': 'no-cache' });
      const send = (event: string, data: unknown) =>
        res.write(`event: ${event}\ndata: ${JSON.stringify(data)}\n\n`);
      send('message_start', {
        type: 'message_start',
        message: {
          ...message,
          content: [],
          stop_reason: null,
          usage: { ...usage, output_tokens: 1 },
        },
      });
      if (block.type === 'text') {
        send('content_block_start', {
          type: 'content_block_start',
          index: 0,
          content_block: { type: 'text', text: '' },
        });
        send('content_block_delta', {
          type: 'content_block_delta',
          index: 0,
          delta: { type: 'text_delta', text: 'Done.' },
        });
      } else {
        send('content_block_start', {
          type: 'content_block_start',
          index: 0,
          content_block: {
            type: 'tool_use',
            id: (block as { id: string }).id,
            name: 'Glob',
            input: {},
          },
        });
        send('content_block_delta', {
          type: 'content_block_delta',
          index: 0,
          delta: { type: 'input_json_delta', partial_json: JSON.stringify({ pattern: '*' }) },
        });
      }
      send('content_block_stop', { type: 'content_block_stop', index: 0 });
      send('message_delta', {
        type: 'message_delta',
        delta: { stop_reason: stop, stop_sequence: null },
        usage: { output_tokens: 50 },
      });
      send('message_stop', { type: 'message_stop' });
      res.end();
    });
  });
  return { server, answered };
}

async function listen(server: Server): Promise<string> {
  await new Promise<void>((resolve) => server.listen(0, '127.0.0.1', () => resolve()));
  return `http://127.0.0.1:${(server.address() as AddressInfo).port}`;
}

async function until(check: () => boolean, ms: number, what: () => string): Promise<void> {
  const deadline = Date.now() + ms;
  while (!check()) {
    if (Date.now() > deadline) throw new Error(`timed out waiting for ${what()}`);
    await delay(50);
  }
}

test('the CLI stops a task at its limit, and Carry On resumes it under a new one that counts from now', {
  skip: SKIP,
  timeout: 240_000,
}, async (t) => {
  const api = fakeApi();
  const base = await listen(api.server);
  t.after(() => {
    api.server.closeAllConnections();
    api.server.close();
  });
  const adapter = new ClaudeAgentRuntimeAdapter({
    directoryPolicy: (path) => ({ ok: true, directory: path }),
    processRecordFile: join(ROOT, 'halcyonic', 'claude-agent-processes.json'),
    inheritedEnvironment: {
      PATH: '/usr/bin:/bin:/usr/sbin:/sbin',
      HOME: join(ROOT, 'home'),
      CLAUDE_CONFIG_DIR: join(ROOT, 'config'),
      TMPDIR: `${join(ROOT, 'tmp')}/`,
      ANTHROPIC_API_KEY: 'sk-ant-dummy-halcyonic-test',
    },
    environment: {
      ANTHROPIC_BASE_URL: base,
      CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC: '1',
      DISABLE_AUTOUPDATER: '1',
      DISABLE_TELEMETRY: '1',
      DISABLE_ERROR_REPORTING: '1',
    },
    pathToClaudeCodeExecutable: BINARY,
    remainingSettings: async () => ({}),
  });
  t.after(() => adapter.close());
  const execution = {
    execution_id: '01920000-0000-7000-8000-000000000810' as ExecutionId,
    workstream_id: '01920000-0000-7000-8000-000000000802' as WorkstreamId,
    project_id: '01920000-0000-7000-8000-000000000801' as ProjectId,
  };
  const observed: RuntimeObservation[] = [];
  const failures = () =>
    observed.flatMap((o) => (o.type === 'runtime.turn.failed' ? [o.payload.error] : []));
  await adapter.startExecution({
    execution,
    instruction: 'Look at the files again and again.',
    options: {},
    model_ref: null,
    directory: join(ROOT, 'project'),
    spending_cap_usd: 0.5,
    emit: (observation) => observed.push(observation),
  });
  await until(
    () => failures().length === 1,
    90_000,
    () => `the stop at the limit; observed ${observed.map((o) => o.type).join(', ')}`,
  );
  const first = api.answered.count;
  assert.equal(failures()[0]?.code, 'spending_cap_reached');
  assert.equal(failures()[0]?.message, 'The app stopped this task at its spending limit.');
  assert.equal(first, 2, 'the CLI checks after each call: the second call passed $0.50');
  await adapter.sendInstruction({ execution, text: 'Carry on.', spending_cap_usd: 1 });
  await until(
    () => failures().length === 2,
    90_000,
    () => `the stop at the new limit; observed ${observed.map((o) => o.type).join(', ')}`,
  );
  assert.equal(failures()[1]?.code, 'spending_cap_reached');
  // $1.00 more from now: three calls of $0.401 pass it, not the two a whole-session count would allow.
  assert.equal(api.answered.count - first, 3);
  assert.ok(!observed.some((o) => o.type === 'runtime.connection.lost'), 'no loss in the relaunch');
});
