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
 * What each turn used, on the real CLI the agent SDK bundles (CLAUDE_CODE_BIN): a fake Messages API
 * on loopback answers every request with the same counts, the cache's reads and writes among them,
 * and each turn's report is the difference of the CLI's running totals. The key is a dummy.
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
const ROOT = realpathSync(mkdtempSync(join(tmpdir(), 'halcyonic-claude-usage-')));
after(() => rmSync(ROOT, { recursive: true, force: true }));
for (const name of ['home', 'config', 'tmp', 'halcyonic', 'project']) mkdirSync(join(ROOT, name));

/** Every answer's counts, as the Messages API gives them. */
const USAGE = { input_tokens: 10, cache_read_input_tokens: 200, cache_creation_input_tokens: 40 };
const OUTPUT_TOKENS = 5;

/** A Messages API that answers Done to everything and counts the answers it streamed. */
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
      const body = JSON.parse(raw) as { stream?: boolean; model?: string };
      answered.count += 1;
      const message = {
        id: `msg_${randomUUID()}`,
        type: 'message',
        role: 'assistant',
        model: body.model,
        stop_sequence: null,
        usage: { ...USAGE, output_tokens: OUTPUT_TOKENS },
      };
      if (body.stream !== true) {
        res.writeHead(200, { 'content-type': 'application/json' }).end(
          JSON.stringify({
            ...message,
            content: [{ type: 'text', text: 'Done.' }],
            stop_reason: 'end_turn',
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
          usage: { ...USAGE, output_tokens: 1 },
        },
      });
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
      send('content_block_stop', { type: 'content_block_stop', index: 0 });
      send('message_delta', {
        type: 'message_delta',
        delta: { stop_reason: 'end_turn', stop_sequence: null },
        usage: { output_tokens: OUTPUT_TOKENS },
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

test("each turn's usage is the CLI's own count of that turn's answers, the cache's apart", {
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
    // This Mac's own managed Claude Code settings are not the test's: none.
    remainingSettings: async () => ({}),
  });
  t.after(() => adapter.close());
  assert.equal(adapter.descriptor.reports_usage, true);
  const execution = {
    execution_id: '01920000-0000-7000-8000-000000000710' as ExecutionId,
    workstream_id: '01920000-0000-7000-8000-000000000702' as WorkstreamId,
    project_id: '01920000-0000-7000-8000-000000000701' as ProjectId,
  };
  const observed: RuntimeObservation[] = [];
  const ended = () => observed.filter((o) => o.type === 'runtime.turn.completed').length;
  const reports = () => observed.flatMap((o) => (o.type === 'runtime.usage.reported' ? [o] : []));
  await adapter.startExecution({
    execution,
    instruction: 'Say done.',
    options: {},
    model_ref: null,
    directory: join(ROOT, 'project'),
    emit: (observation) => observed.push(observation),
  });
  await until(
    () => ended() === 1,
    90_000,
    () => `the first turn to finish; observed ${observed.map((o) => o.type).join(', ')}`,
  );
  const first = api.answered.count;
  await adapter.sendInstruction({ execution, text: 'Say done again.' });
  await until(
    () => ended() === 2,
    90_000,
    () => `the second turn to finish; observed ${observed.map((o) => o.type).join(', ')}`,
  );
  const second = api.answered.count - first;
  assert.ok(first >= 1 && second >= 1, 'each turn asked the API');
  const [one, two] = reports();
  assert.equal(reports().length, 2, 'one report for each turn');
  const counts = (answers: number) => ({
    input: USAGE.input_tokens * answers,
    cache_read: USAGE.cache_read_input_tokens * answers,
    cache_write: USAGE.cache_creation_input_tokens * answers,
    output: OUTPUT_TOKENS * answers,
  });
  const { reasoning: _one, ...oneTokens } = one?.payload.tokens ?? {};
  const { reasoning: _two, ...twoTokens } = two?.payload.tokens ?? {};
  assert.deepEqual(oneTokens, counts(first));
  assert.deepEqual(twoTokens, counts(second), 'the second turn alone, not the running total');
  assert.ok([one, two].every((report) => report?.provenance.epistemic === 'reported'));
  // Claude Code's own estimate, by its list prices for the model it asked for.
  for (const report of [one, two]) {
    assert.equal(report?.payload.cost?.prices, 'list');
    assert.ok((report?.payload.cost?.estimated_usd ?? 0) > 0);
  }
});
