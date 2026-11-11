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
 * An instruction's pictures (A4), on the real CLI the agent SDK bundles (CLAUDE_CODE_BIN): each
 * reaches the Messages API as a base64 image block of the bytes the host read, before the words. A
 * fake Messages API on loopback answers and keeps each request's body; the key is a dummy.
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
const ROOT = realpathSync(mkdtempSync(join(tmpdir(), 'halcyonic-claude-pictures-')));
after(() => rmSync(ROOT, { recursive: true, force: true }));
for (const name of ['home', 'config', 'tmp', 'halcyonic', 'project']) mkdirSync(join(ROOT, name));

/** A 1 by 1 PNG, whole. */
const PNG = Buffer.from(
  'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAIAAACQd1PeAAAADElEQVR4nGP4z8AAAAMBAQDJ/pLvAAAAAElFTkSuQmCC',
  'base64',
);

/** A Messages API that answers Done to everything and keeps each request's body. */
function fakeApi() {
  const bodies: string[] = [];
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
      bodies.push(raw);
      const body = JSON.parse(raw) as { stream?: boolean; model?: string };
      const message = {
        id: `msg_${randomUUID()}`,
        type: 'message',
        role: 'assistant',
        model: body.model,
        stop_sequence: null,
        usage: { input_tokens: 10, output_tokens: 1 },
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
        message: { ...message, content: [], stop_reason: null },
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
        usage: { output_tokens: 1 },
      });
      send('message_stop', { type: 'message_stop' });
      res.end();
    });
  });
  return { server, bodies };
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

test("an instruction's pictures reach the API as base64 image blocks before its words", {
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
  });
  t.after(() => adapter.close());
  assert.equal(adapter.descriptor.takes_pictures, true);
  const observed: RuntimeObservation[] = [];
  await adapter.startExecution({
    execution: {
      execution_id: '01920000-0000-7000-8000-000000000610' as ExecutionId,
      workstream_id: '01920000-0000-7000-8000-000000000602' as WorkstreamId,
      project_id: '01920000-0000-7000-8000-000000000601' as ProjectId,
    },
    instruction: 'Paint it like MARKER-PICTURE.',
    pictures: [
      {
        path: 'art/dot.png',
        media_type: 'image/png',
        bytes: new Uint8Array(PNG),
        sha256: 'e'.repeat(64),
      },
    ],
    options: {},
    model_ref: null,
    directory: join(ROOT, 'project'),
    emit: (observation) => observed.push(observation),
  });
  await until(
    () => observed.some((o) => o.type === 'runtime.turn.completed'),
    90_000,
    () => `the turn to finish; observed ${observed.map((o) => o.type).join(', ')}`,
  );
  const sent = api.bodies
    .map((body) => JSON.parse(body) as { messages: { role: string; content: unknown }[] })
    .find((body) => JSON.stringify(body).includes('MARKER-PICTURE'));
  assert.ok(sent !== undefined, 'the instruction reached the API');
  const user = sent.messages.find((message) =>
    JSON.stringify(message.content).includes('MARKER-PICTURE'),
  );
  const content = (user?.content ?? []) as { type: string; source?: unknown }[];
  assert.deepEqual(content.find((block) => block.type === 'image')?.source, {
    type: 'base64',
    media_type: 'image/png',
    data: PNG.toString('base64'),
  });
  assert.ok(
    content.findIndex((block) => block.type === 'image') <
      content.findIndex(
        (block) => block.type === 'text' && JSON.stringify(block).includes('MARKER-PICTURE'),
      ),
    'the picture goes before the words',
  );
});
